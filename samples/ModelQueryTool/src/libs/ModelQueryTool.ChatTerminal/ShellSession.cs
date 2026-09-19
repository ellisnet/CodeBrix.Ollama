using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.Editing;
using ModelQueryTool.ChatTerminal.IO;

namespace ModelQueryTool.ChatTerminal;

/// <summary>
/// The session's brain: consumes VT-encoded input from a terminal view, edits
/// the current line (with history), and hands submitted lines to the active
/// <see cref="ILineInterpreter"/> — the chat interpreter in this application,
/// or a pushed sub-mode. All output (echo, prompts, results) is raised through
/// <see cref="OutputProduced"/> as VT data for the terminal view to display.
/// </summary>
/// <remarks>
/// Threading: <see cref="SendInput"/> must be called from one thread at a time
/// (the UI thread, in practice). Lines execute sequentially on worker threads,
/// so <see cref="OutputProduced"/> is raised on the input thread for echo and
/// on worker threads for command output — the attached view must marshal.
/// </remarks>
public sealed class ShellSession
{
    private readonly InputTokenizer _tokenizer = new();
    private readonly LineEditor _editor = new();
    private readonly List<string> _history = [];
    private readonly Stack<ILineInterpreter> _interpreters = new();
    private readonly object _gate = new();

    private Task _executionChain = Task.CompletedTask;
    private CancellationTokenSource _activeCts;
    private int _queuedLines;
    private int _historyIndex;
    private string _pendingLine = string.Empty;
    private bool _lastInputWasCr;
    private bool _started;
    private char _pendingHighSurrogate;

    /// <summary>
    /// Creates a session whose root interpreter dispatches the commands in the
    /// given registry.
    /// </summary>
    /// <param name="commands">The commands the session dispatches.</param>
    /// <param name="options">The session options; null takes the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is null.</exception>
    public ShellSession(CommandRegistry commands, ShellSessionOptions options = null)
    {
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        Options = options ?? new ShellSessionOptions();
        Output = new DelegateShellIO(RaiseOutput);
        _interpreters.Push(new CommandInterpreter(Commands, Options.Prompt));
    }

    /// <summary>
    /// Creates a session over a root interpreter of the caller's own - a chat's
    /// <see cref="ChatLineInterpreter"/>, for which the conversation is the root
    /// and commands are the special case.
    /// </summary>
    /// <param name="rootInterpreter">The interpreter every submitted line goes to.</param>
    /// <param name="options">
    /// The session options; null takes the defaults. The interpreter's own
    /// <see cref="ILineInterpreter.Prompt"/> is what the session writes -
    /// <see cref="ShellSessionOptions.Prompt"/> is not consulted on this path.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rootInterpreter"/> is null.</exception>
    public ShellSession(ILineInterpreter rootInterpreter, ShellSessionOptions options = null)
    {
        if (rootInterpreter == null) { throw new ArgumentNullException(nameof(rootInterpreter)); }

        Options = options ?? new ShellSessionOptions();
        Output = new DelegateShellIO(RaiseOutput);
        _interpreters.Push(rootInterpreter);
    }

    /// <summary>
    /// Raised with VT data to display. Echo is raised on the input thread;
    /// command output on worker threads. The terminal view marshals.
    /// </summary>
    public event Action<string> OutputProduced;

    /// <summary>The output surface commands and interpreters write to.</summary>
    public IShellIO Output { get; }

    /// <summary>
    /// The registered commands, when the session was created over a registry;
    /// null when it was created over a root interpreter, which owns its own
    /// (see <see cref="ChatLineInterpreter.Commands"/>).
    /// </summary>
    public CommandRegistry Commands { get; }

    /// <summary>The session options.</summary>
    public ShellSessionOptions Options { get; }

    /// <summary>The prompt of the active interpreter.</summary>
    public string Prompt => CurrentInterpreter.Prompt;

    /// <summary>
    /// The terminal's column count, which the line editor lays the input line
    /// out against. 80 until <see cref="SetGridSize"/> says otherwise.
    /// </summary>
    public int Columns => _editor.Columns;

    /// <summary>
    /// The terminal's row count, as last reported to <see cref="SetGridSize"/>.
    /// The session records it but does not use it: a line taller than the screen
    /// is a documented limitation of <see cref="LineEditor"/>, not something the
    /// session can repair.
    /// </summary>
    public int Rows { get; private set; }

    /// <summary>True while a submitted line is executing.</summary>
    public bool IsExecuting => _activeCts != null;

    /// <summary>True while a submitted line is executing or queued.</summary>
    public bool IsBusy => _activeCts != null || Volatile.Read(ref _queuedLines) > 0;

    /// <summary>The chain of queued/executing lines — awaited by tests to reach quiescence.</summary>
    internal Task ExecutionChain
    {
        get { lock (_gate) { return _executionChain; } }
    }

    private ILineInterpreter CurrentInterpreter
    {
        get { lock (_interpreters) { return _interpreters.Peek(); } }
    }

    /// <summary>Writes the banner (if any) and the first prompt.</summary>
    public void Start()
    {
        _started = true;
        if (Options.Banner is { Length: > 0 })
        {
            foreach (var line in Options.Banner) { Output.WriteLine(line); }
        }

        WritePrompt();
    }

    /// <summary>
    /// Enters a sub-mode: subsequent lines go to the pushed interpreter until
    /// it is popped (or the user presses Ctrl+D on an empty line).
    /// </summary>
    public void PushInterpreter(ILineInterpreter interpreter)
    {
        if (interpreter == null) { throw new ArgumentNullException(nameof(interpreter)); }
        lock (_interpreters) { _interpreters.Push(interpreter); }
    }

    /// <summary>Leaves the current sub-mode. The root interpreter is never popped.</summary>
    public void PopInterpreter()
    {
        lock (_interpreters)
        {
            if (_interpreters.Count > 1) { _interpreters.Pop(); }
        }
    }

    /// <summary>
    /// Tells the session how big the terminal's grid is - from the control's
    /// column count when the terminal is first ready, and from its resize event
    /// afterwards. The input line being edited is repainted against the new
    /// width, so a resize mid-line leaves no stale rows behind.
    /// </summary>
    /// <param name="columns">The terminal's column count (at least 1).</param>
    /// <param name="rows">The terminal's row count, recorded for the caller's benefit.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> is below 1.</exception>
    /// <remarks>
    /// <para>
    /// Call from the same thread that calls <see cref="SendInput"/> - the
    /// control raises both on the UI thread.
    /// </para>
    /// <para>
    /// NOTHING IS REPAINTED WHILE A SUBMITTED LINE IS RUNNING. There is no prompt
    /// on screen then - the running line's output is - so a repaint would write a
    /// prompt, and the attribute reset it carries, into the middle of that output.
    /// The new width is only recorded, and the prompt the line's completion prints
    /// is laid out against it. This is not a rare case: a terminal control that
    /// shows its scrollbar the first time its content scrolls loses a column or two
    /// at exactly that moment, which is always in the middle of a long answer.
    /// </para>
    /// </remarks>
    public void SetGridSize(int columns, int rows)
    {
        if (columns < 1) { throw new ArgumentOutOfRangeException(nameof(columns), "A terminal has at least one column."); }

        Rows = rows;
        if (columns == _editor.Columns) { return; }

        if (!_started || IsBusy)
        {
            //Nothing to repaint: either nothing has been written yet, or what is on
            //  screen is a running line's output rather than the prompt
            _editor.Columns = columns;
            return;
        }

        //The wind-back works from the OLD layout and the new width; the redraw
        //  from the new one
        var rewind = _editor.BeginRepaint(columns);
        _editor.Columns = columns;
        Echo(rewind);
        WritePrompt();
        Echo(_editor.Redraw());
    }

    /// <summary>
    /// Feeds VT-encoded user input into the session: one keystroke, or one
    /// pasted block, as the terminal control delivered it.
    /// </summary>
    /// <param name="data">The chunk of input, exactly as it arrived.</param>
    /// <remarks>
    /// Whether a line ending in <paramref name="data"/> submits the line or
    /// becomes a line break inside it is <see cref="PasteRule"/>'s decision, so
    /// pasting several lines asks one question instead of several. Escape
    /// sequences may be split across calls; the tokenizer holds the unfinished
    /// part.
    /// </remarks>
    public void SendInput(string data)
    {
        var lineEndingsAreText = !PasteRule.IsEnterKey(data);

        foreach (var token in _tokenizer.Feed(data))
        {
            switch (token.Kind)
            {
                case InputTokenKind.Character:
                    _lastInputWasCr = false;
                    HandleCharacter(token.Character);
                    break;

                case InputTokenKind.Control:
                    HandleControl(token.Character, lineEndingsAreText);
                    break;

                case InputTokenKind.Key:
                    _lastInputWasCr = false;
                    FlushPendingSurrogate();
                    HandleKey(token.Key);
                    break;
            }
        }
    }

    /// <summary>
    /// Inserts a typed character, holding a high surrogate back until its low
    /// half arrives so that the pair reaches the editor as one character - the
    /// tokenizer works in UTF-16 units and delivers the two halves separately.
    /// The hold survives the end of a chunk, as the escape-sequence one does, so
    /// a pair split across two deliveries still arrives whole; a half that is
    /// never completed is written as it is by the next character or key.
    /// </summary>
    private void HandleCharacter(char c)
    {
        ResetHistoryNavigation();

        if (_pendingHighSurrogate != '\0')
        {
            var high = _pendingHighSurrogate;
            _pendingHighSurrogate = '\0';
            if (char.IsLowSurrogate(c))
            {
                Echo(_editor.Insert(high.ToString() + c));
                return;
            }

            Echo(_editor.Insert(high));
        }

        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return;
        }

        Echo(_editor.Insert(c));
    }

    private void FlushPendingSurrogate()
    {
        if (_pendingHighSurrogate == '\0') { return; }

        var high = _pendingHighSurrogate;
        _pendingHighSurrogate = '\0';
        Echo(_editor.Insert(high));
    }

    private void HandleControl(char c, bool lineEndingsAreText)
    {
        var wasCr = _lastInputWasCr;
        _lastInputWasCr = c == '\r';

        if (c is '\r' or '\n')
        {
            FlushPendingSurrogate();
        }

        switch (c)
        {
            case '\r':
                if (lineEndingsAreText) { InsertLineBreak(); }
                else { SubmitLine(); }
                break;

            case '\n':
                //Half of a CRLF pair that the CR already dealt with
                if (wasCr) { break; }
                if (lineEndingsAreText) { InsertLineBreak(); }
                else { SubmitLine(); }
                break;

            case '\b':
            case '\x7f':
                ResetHistoryNavigation();
                Echo(_editor.Backspace());
                break;

            case '\x03': //Ctrl+C
                if (_activeCts is { } cts)
                {
                    cts.Cancel();
                }
                else
                {
                    //Past the end of the line first: the rows below the cursor
                    //  belong to the line being abandoned
                    Echo(_editor.MoveEnd());
                    Echo("^C\r\n");
                    _editor.Reset();
                    ResetHistoryNavigation();
                    WritePrompt();
                }
                break;

            case '\x04': //Ctrl+D - leave a sub-mode when the line is empty
                if (_editor.Text.Length == 0 && InterpreterDepth > 1)
                {
                    Echo("\r\n");
                    PopInterpreter();
                    WritePrompt();
                }
                break;

            case '\x0c': //Ctrl+L - clear screen, repaint prompt and line
                Echo("\x1b[2J\x1b[H");
                WritePrompt();
                Echo(_editor.Redraw());
                break;
        }
    }

    private void HandleKey(EditKey key)
    {
        switch (key)
        {
            case EditKey.Left: Echo(_editor.MoveLeft()); break;
            case EditKey.Right: Echo(_editor.MoveRight()); break;
            case EditKey.Home: Echo(_editor.MoveHome()); break;
            case EditKey.End: Echo(_editor.MoveEnd()); break;

            case EditKey.Delete:
                ResetHistoryNavigation();
                Echo(_editor.Delete());
                break;

            case EditKey.Up:
                if (_historyIndex > 0)
                {
                    if (_historyIndex == _history.Count) { _pendingLine = _editor.Text; }
                    _historyIndex--;
                    Echo(_editor.ReplaceWith(_history[_historyIndex]));
                }
                break;

            case EditKey.Down:
                if (_historyIndex < _history.Count)
                {
                    _historyIndex++;
                    var text = _historyIndex == _history.Count
                        ? _pendingLine
                        : _history[_historyIndex];
                    Echo(_editor.ReplaceWith(text));
                }
                break;

            //PageUp/PageDown are the view's scrollback keys - nothing to do here
        }
    }

    private int InterpreterDepth
    {
        get { lock (_interpreters) { return _interpreters.Count; } }
    }

    private void InsertLineBreak()
    {
        ResetHistoryNavigation();
        Echo(_editor.InsertLineBreak());
    }

    private void SubmitLine()
    {
        //Past the end first, or the rows of the line below the cursor would be
        //  left on screen for the answer to write over
        Echo(_editor.MoveEnd());

        //A line that ended exactly at the right edge already put the cursor on a
        //  fresh row; another CR+LF there would leave a blank one behind
        var onAFreshRow = _editor.CursorColumn == 0 && _editor.RowCount > 1;
        var line = _editor.TakeLine();
        Echo(onAFreshRow ? string.Empty : "\r\n");

        if (line.Trim().Length > 0 &&
            (_history.Count == 0 || _history[^1] != line))
        {
            _history.Add(line);
        }

        ResetHistoryNavigation();
        EnqueueLine(line);
    }

    private void ResetHistoryNavigation()
    {
        _historyIndex = _history.Count;
        _pendingLine = string.Empty;
    }

    private void EnqueueLine(string line)
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _queuedLines);
            var previous = _executionChain;
            //Task.Run keeps interpreter work off the input (UI) thread even
            //  when the previous chain link has already completed.
            _executionChain = Task.Run(() => RunAfterAsync(previous, line));
        }
    }

    private async Task RunAfterAsync(Task previous, string line)
    {
        await previous.ConfigureAwait(false);
        await ExecuteLineAsync(line).ConfigureAwait(false);
    }

    private async Task ExecuteLineAsync(string line)
    {
        var interpreter = CurrentInterpreter;
        var cts = new CancellationTokenSource();
        _activeCts = cts;
        try
        {
            await interpreter.HandleLineAsync(this, line, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Output.WriteLine("^C");
        }
        catch (Exception ex)
        {
            Output.WriteLine("error: " + DeepestMessage(ex));
        }
        finally
        {
            _activeCts = null;
            cts.Dispose();
            WritePrompt();
            //Decrement AFTER the prompt: an out-of-band message racing this
            //  completion then writes message-only instead of a second prompt
            Interlocked.Decrement(ref _queuedLines);
        }
    }

    /// <summary>
    /// Writes message lines that arrive outside any line execution (a
    /// background model load finishing, for example). Idle at a prompt: the
    /// message takes the input line's PLACE and the input line comes back
    /// underneath it, with its text and its cursor exactly as they were. While
    /// a line is executing or queued: writes the lines only - the running
    /// line's own completion prints the next prompt.
    /// </summary>
    /// <param name="lines">The lines to write; null writes none.</param>
    /// <remarks>
    /// <para>
    /// THE INPUT LINE IS WOUND BACK, NOT PUSHED DOWN. Opening a fresh row under
    /// the prompt and printing the prompt again below the message would leave
    /// the old prompt row - and any half-typed text on it - behind as a stale
    /// copy, so three messages during start-up would stack up three prompts
    /// before the user had typed anything. The editor's own wind-back
    /// (<see cref="LineEditor.BeginRepaint()"/>) returns to the start of the
    /// prompt and erases downward, however many rows the line being typed
    /// occupies, and the redraw afterwards puts every row of it back once.
    /// </para>
    /// <para>
    /// Before <see cref="Start"/> there is no prompt on screen to take the
    /// place of, so the lines are written on their own. Call from the same
    /// thread that calls <see cref="SendInput"/>.
    /// </para>
    /// </remarks>
    public void WriteOutOfBand(params string[] lines)
    {
        var idle = _started && !IsBusy;

        if (idle) { Echo(_editor.BeginRepaint()); }

        if (lines != null)
        {
            foreach (var line in lines) { Output.WriteLine(line); }
        }

        if (idle) { RepaintPrompt(); }
    }

    /// <summary>
    /// The innermost exception message — wrapper exceptions ("evaluation
    /// failed on thread X") hide the message the user actually needs.
    /// </summary>
    public static string DeepestMessage(Exception exception)
    {
        while (exception.InnerException != null) { exception = exception.InnerException; }
        return exception.Message;
    }

    /// <summary>
    /// Reprints the prompt and the line being edited — call after out-of-band
    /// output (a background task finishing) interrupted the input line. Call
    /// from the same thread that calls <see cref="SendInput"/>.
    /// </summary>
    public void RepaintPrompt()
    {
        WritePrompt();
        Echo(_editor.Redraw());
    }

    /// <summary>
    /// Writes the active interpreter's prompt, telling the editor how wide it
    /// is first: the editor's row arithmetic starts where the prompt ends, and
    /// a prompt that carries its own colour is narrower than its string length.
    /// </summary>
    private void WritePrompt()
    {
        var prompt = Prompt;
        _editor.PromptWidth = CellWidth.OfText(prompt);
        Output.Write(prompt);
    }

    private void Echo(string text)
    {
        if (!string.IsNullOrEmpty(text)) { RaiseOutput(text); }
    }

    private void RaiseOutput(string text) => OutputProduced?.Invoke(text);
}
