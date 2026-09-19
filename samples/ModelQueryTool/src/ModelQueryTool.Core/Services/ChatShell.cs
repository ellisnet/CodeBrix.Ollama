using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.ChatTerminal;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.IO;
using ModelQueryTool.ChatTerminal.Output;
using ModelQueryTool.Helpers;
using ModelQueryTool.ModelAccess;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using ModelQueryTool.Services.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModelQueryTool.Services;

/// <summary>
/// The whole chat, as a plain class: the line the user is typing, the commands a slash starts,
/// the turn the model is answering, and the long pieces of work - obtaining the model and
/// loading it - that a status bar reports.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING HERE KNOWS ABOUT A USER INTERFACE. Text leaves through one delegate, everything the
/// chat cannot do itself arrives through <see cref="IChatShellHost"/>, and the two model
/// libraries arrive as their interfaces. That is what lets the whole of the chat's behaviour -
/// every command, every byte of a turn - be driven from a test with no window, no model and no
/// network.
/// </para>
/// <para>
/// THREADING. <see cref="Start"/>, <see cref="SendInput"/>, <see cref="SetGridSize"/> and
/// <see cref="Cancel"/> belong to the thread the terminal raises its input on. Submitted lines
/// run on worker threads, the download reports on its own thread and the engine reports a load
/// on its loading thread; everything those write goes through one
/// <see cref="OutputBatcher"/>, which is safe from any thread, and everything they show goes
/// through <see cref="IChatShellHost"/>, whose implementation marshals.
/// </para>
/// <para>
/// NOTHING IS KEPT BETWEEN RUNS. There is no settings file, no saved conversation and no
/// history on disk: each launch starts with reasoning on, the default context size, no system
/// prompt and an empty conversation, and learns what is on disk by asking the stager to look.
/// </para>
/// <para>
/// WHAT IT KEEPS WHILE IT RUNS is the conversation as the model wrote it: each turn's prompt and
/// the ANSWER'S OWN TEXT, joined from the pieces as they stream, with no wrapping and no escape
/// sequence in it. That is what <c>/copy</c> hands to the clipboard, and it is why copying with
/// the mouse - which carries a line break at every row the wrapper broke - is not the same thing.
/// The reasoning is never kept.
/// </para>
/// </remarks>
public sealed class ChatShell : IDisposable
{
    /// <summary>The one line that says reasoning can be turned off, written once per turn that reasons.</summary>
    internal const string ThinkingReminder = "Thinking is on; /think off turns it off.";

    /// <summary>The prompt's label, in front of the bracket the user types after.</summary>
    private const string PromptLabel = "chat";

    /// <summary>The width the chat lays text out against until the terminal says what it really is.</summary>
    private const int DefaultColumns = 80;

    /// <summary>What the terminal sends for Ctrl+C.</summary>
    private const string Interrupt = "\x03";

    /// <summary>What labels the user's own words in a copy of the whole conversation.</summary>
    private const string YouLabel = "You:";

    /// <summary>What labels the model's words in a copy of the whole conversation.</summary>
    private const string ModelLabel = "Model:";

    /// <summary>What a copied conversation puts between one turn and the next.</summary>
    private const string TurnSeparator = "\n\n";

    private static readonly TimeSpan OutputInterval = TimeSpan.FromMilliseconds(50);

    private readonly IModelStager _stager;
    private readonly IModelHost _host;
    private readonly IChatShellHost _shellHost;
    private readonly OutputBatcher _batcher;
    private readonly ShellSession _session;
    private readonly StreamingWordWrapper _wrapper;
    private readonly CommandRegistry _commands;
    private readonly CommandHighlights _highlights;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<ChatTurn> _turns = [];
    private readonly object _gate = new();

    private Task _background = Task.CompletedTask;
    private Task _shutdown;
    private CancellationTokenSource _operation;
    private ChatBusyKind _busy;
    private GenerationStatistics _lastStatistics;
    private FinishReason _lastFinishReason;
    private int _columns = DefaultColumns;
    private bool _thinkingOpen;
    private bool _turnWaitShown;
    private bool _started;
    private bool _disposed;

    /// <summary>Builds the chat over the two model services and the host that shows it.</summary>
    /// <param name="stager">The model on disk: what is there, how to get it, how to take it away.</param>
    /// <param name="host">The model in memory: loading it, talking to it, unloading it.</param>
    /// <param name="output">
    /// Where every byte the chat writes goes - the terminal control's feed, in the application.
    /// It is called from worker threads as well as from the terminal's own, in batches.
    /// </param>
    /// <param name="shellHost">The status bar, the way onto the terminal's thread, and the way out.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ChatShell(IModelStager stager, IModelHost host, Action<string> output, IChatShellHost shellHost)
    {
        if (output == null) { throw new ArgumentNullException(nameof(output)); }

        _stager = stager ?? throw new ArgumentNullException(nameof(stager));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));

        _batcher = new OutputBatcher(output, OutputInterval);
        _wrapper = new StreamingWordWrapper(DefaultColumns);
        _commands = new CommandRegistry();
        _highlights = new CommandHighlights(_commands);

        //Before the banner is built, so that the commands it names are already registered: the
        //  highlights read the registry late, but a finished line is a finished line
        RegisterCommands();

        var interpreter = new ChatLineInterpreter(
            _commands, HandleChatLineAsync, TerminalText.Prompt(PromptLabel), _highlights);
        _session = new ShellSession(interpreter, new ShellSessionOptions { Banner = BuildBanner() });
        _session.OutputProduced += _batcher.Append;
    }

    /// <summary>Gets the model on disk, for the commands that report on it or change it.</summary>
    internal IModelStager Stager => _stager;

    /// <summary>Gets the model in memory, for the commands that report on it or change it.</summary>
    internal IModelHost Host => _host;

    /// <summary>Gets the surface a command writes its answer to.</summary>
    internal IShellIO IO => _session.Output;

    /// <summary>Gets the terminal's width in cells, which every line the chat writes is laid out against.</summary>
    internal int Columns => _columns;

    /// <summary>Gets the long piece of work in flight, or none.</summary>
    internal ChatBusyKind Busy
    {
        get { lock (_gate) { return _busy; } }
    }

    /// <summary>Gets what the last completed turn cost, or null when no turn has completed.</summary>
    internal GenerationStatistics LastStatistics
    {
        get { lock (_gate) { return _lastStatistics; } }
    }

    /// <summary>Gets why the last completed turn ended.</summary>
    internal FinishReason LastFinishReason
    {
        get { lock (_gate) { return _lastFinishReason; } }
    }

    /// <summary>
    /// Gets whether the model has been put away. Every member of the host throws once it has
    /// been disposed, property getters included, so anything that would read it asks here first.
    /// </summary>
    internal bool IsShutDown
    {
        get { lock (_gate) { return _shutdown != null; } }
    }

    /// <summary>
    /// Tells the chat how wide the terminal is: its column count when the terminal is first
    /// ready, and again whenever it changes. The line being edited is repainted at the new
    /// width, and everything written afterwards is wrapped to it.
    /// </summary>
    /// <param name="columns">The terminal's column count.</param>
    /// <param name="rows">The terminal's row count.</param>
    /// <remarks>Call from the thread the terminal raises its input on.</remarks>
    public void SetGridSize(int columns, int rows)
    {
        if (columns < 1) { return; }

        _columns = columns;
        _wrapper.Columns = columns;
        _session.SetGridSize(columns, rows);
    }

    /// <summary>
    /// Starts the chat: the banner and the first prompt, and then - in the background, so the
    /// prompt is usable at once - a look at what is on disk and, when the model is there, a
    /// load of it.
    /// </summary>
    /// <remarks>
    /// Call once, from the thread the terminal raises its input on, and only after the terminal
    /// is on screen: anything fed to the control before that is dropped.
    /// </remarks>
    public void Start()
    {
        if (_started || _disposed) { return; }

        _started = true;
        _session.Start();
        _background = Task.Run(StartUpAsync);
    }

    /// <summary>Hands the chat one chunk of terminal input, exactly as the control delivered it.</summary>
    /// <param name="data">The chunk: a keystroke, a pasted block, or a line ending.</param>
    /// <remarks>
    /// Call from the thread the terminal raises its input on. Ctrl+C is the one chunk that does
    /// not simply go through: to the session it means "stop the running line, or clear the one
    /// being typed", and while the chat is loading a model in the background there is no running
    /// line for it to stop - so it is the load the user means, and the load is what stops.
    /// </remarks>
    public void SendInput(string data)
    {
        if (data == Interrupt && HasOperation)
        {
            Cancel();

            return;
        }

        _session.SendInput(data);
    }

    /// <summary>
    /// Stops whatever long thing is running: a download, a load, or the turn the model is in the
    /// middle of. It is what the Cancel button does, and what Ctrl+C does when the user types it.
    /// </summary>
    /// <remarks>Call from the thread the terminal raises its input on.</remarks>
    public void Cancel()
    {
        CancellationTokenSource operation;

        lock (_gate)
        {
            operation = _operation;
        }

        if (operation != null)
        {
            try
            {
                operation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                //The work finished between reading its token source and cancelling it.
            }

            return;
        }

        //Nothing long of the chat's own is running, so this is a turn or a command: Ctrl+C is
        //  what the session turns into that line's cancellation.
        _session.SendInput(Interrupt);
    }

    private bool HasOperation
    {
        get { lock (_gate) { return _operation != null; } }
    }

    /// <summary>
    /// Stops the model and unloads it. Every further call returns the same task, so closing the
    /// window after typing /exit unloads nothing twice.
    /// </summary>
    /// <returns>A task that completes when the model is unloaded.</returns>
    public Task ShutdownAsync()
    {
        lock (_gate)
        {
            _shutdown ??= ShutdownCoreAsync();

            return _shutdown;
        }
    }

    /// <summary>
    /// Stops the background work and flushes whatever text is still held. It does not unload the
    /// model - that is <see cref="ShutdownAsync"/>, which has to be awaited.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        //The lifetime source is cancelled but not disposed: background work may still be holding
        //  a token taken from it, and disposing it under them buys nothing at the end of a run.
        _lifetime.Cancel();
        _batcher.Dispose();
    }

    #region | What the commands use |

    /// <summary>
    /// Writes a remark of the chat's own as a dimmed line, wrapped to the terminal's width, with
    /// the commands it names picked out.
    /// </summary>
    /// <param name="message">The remark.</param>
    internal void WriteNotice(string message) => Write(TerminalText.Notice(Wrap(message), _highlights));

    /// <summary>
    /// Writes a failure as a red line, wrapped to the terminal's width, with the commands it names
    /// picked out.
    /// </summary>
    /// <param name="message">The failure, written for a person.</param>
    internal void WriteError(string message) => Write(TerminalText.Error(Wrap(message), _highlights));

    /// <summary>
    /// Writes an ordinary line, wrapped to the terminal's width, with the commands it names picked
    /// out.
    /// </summary>
    /// <param name="message">The line; an empty one is a blank row.</param>
    internal void WriteLine(string message) => IO.WriteLine(TerminalText.Plain(Wrap(message), _highlights));

    /// <summary>Lays text out against the terminal's width, breaking rows between words.</summary>
    /// <param name="text">The text to lay out.</param>
    /// <returns>The text with the row breaks a terminal of this width needs.</returns>
    /// <remarks>
    /// WRAP FIRST, STYLE AFTERWARDS. The wrapper is given plain text and nothing else: it counts
    /// characters, and an escape sequence counted as characters would break the rows in the wrong
    /// places. The colours and the command highlights go on the wrapped text.
    /// </remarks>
    internal string Wrap(string text)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }

        var wrapper = new StreamingWordWrapper(_columns);

        return wrapper.Write(text) + wrapper.Flush();
    }

    /// <summary>
    /// Says so, and answers true, when a long piece of work is in the middle of running - which
    /// is when a command that would touch the model or the store has to wait its turn.
    /// </summary>
    /// <returns>True when the caller should do nothing.</returns>
    internal bool RefuseWhenBusy()
    {
        switch (Busy)
        {
            case ChatBusyKind.Downloading:
                WriteNotice("A download is running; try this again when it has finished. /status and /help work meanwhile.");
                return true;

            case ChatBusyKind.Loading:
                WriteNotice("The model is loading; try this again when it is ready. /status and /help work meanwhile.");
                return true;

            default:
                return false;
        }
    }

    /// <summary>Obtains the model, reporting on the status bar, and then loads what arrived.</summary>
    /// <param name="cancellationToken">Cancels the download, and then the load.</param>
    /// <returns>A task that completes when there is nothing left to do.</returns>
    internal async Task DownloadAndLoadAsync(CancellationToken cancellationToken)
    {
        ModelStagingStatus status;

        using (var operation = BeginOperation(ChatBusyKind.Downloading, cancellationToken))
        {
            try
            {
                var tracker = new DownloadProgressTracker();
                var clock = Stopwatch.StartNew();
                var caption = "Downloading " + _stager.Model.DisplayName + "...";
                _shellHost.ShowProgress(caption, 0d, false);

                var progress = new DelegateProgress<ModelStagingProgress>(report =>
                {
                    if (tracker.TryFormat(
                        report.OverallCompletedBytes, report.OverallTotalBytes, clock.Elapsed, out var updated))
                    {
                        caption = updated;
                    }

                    _shellHost.ShowProgress(caption, report.OverallPercent, false);
                });

                status = await _stager.StageAsync(progress, operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                WriteNotice("The download was stopped. What has arrived is kept - /download -y carries on from there.");
                return;
            }
            finally
            {
                EndOperation();
            }
        }

        WriteNotice(_stager.Model.DisplayName + " is downloaded (" + ByteSize.Describe(status.BytesOnDisk)
            + " in " + status.StoreDirectory + ").");

        await LoadModelAsync(status.ModelPath, cancellationToken).ConfigureAwait(false);
        WriteNotice("The model is loaded and ready.");
    }

    /// <summary>Loads the model, reporting the load on the status bar.</summary>
    /// <param name="modelPath">The model file to load.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    internal async Task LoadModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        //A model that is loaded starts a conversation of its own, so there is nothing of an
        //  earlier one left to copy
        ForgetConversation();

        using var operation = BeginOperation(ChatBusyKind.Loading, cancellationToken);

        try
        {
            await _host.StartAsync(modelPath, BeginLoadProgress("Loading "), operation.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>Loads the model again at another context size, reporting the load on the status bar.</summary>
    /// <param name="contextSize">The context size to load at.</param>
    /// <param name="cancellationToken">Cancels the reload.</param>
    /// <returns>A task that completes when a model is loaded again.</returns>
    internal async Task ReloadModelAsync(uint contextSize, CancellationToken cancellationToken)
    {
        //A reload ends the conversation, so what was said in it is no longer there to copy
        ForgetConversation();

        using var operation = BeginOperation(ChatBusyKind.Loading, cancellationToken);

        try
        {
            await _host.ReloadAsync(contextSize, BeginLoadProgress("Reloading "), operation.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>Closes the application.</summary>
    internal void RequestExit() => _shellHost.Exit();

    /// <summary>
    /// Asks the head to put text on the clipboard. The chat never touches a clipboard itself -
    /// that belongs to whatever is hosting it.
    /// </summary>
    /// <param name="text">The text to copy, verbatim.</param>
    /// <returns>True when it is on the clipboard.</returns>
    internal Task<bool> CopyToClipboardAsync(string text) => _shellHost.CopyTextAsync(text);

    /// <summary>
    /// The model's last answer as the model wrote it: the pieces of the turn joined, with no
    /// wrapping, no reasoning and no escape sequence in it.
    /// </summary>
    /// <returns>The answer, or nothing at all when no turn has produced one.</returns>
    internal ChatCopyText LastAnswer()
    {
        lock (_gate)
        {
            if (_turns.Count == 0) { return ChatCopyText.Nothing; }

            var turn = _turns[_turns.Count - 1];

            return new ChatCopyText(turn.Answer, 1, turn.IsComplete);
        }
    }

    /// <summary>
    /// The whole conversation since it last started afresh, laid out for a person to read and
    /// paste: each turn as a <c>You:</c> block and a <c>Model:</c> block, one blank row between
    /// the two and one between the turns, and no line ending at the end.
    /// </summary>
    /// <returns>The conversation, or nothing at all when no turn has produced an answer.</returns>
    internal ChatCopyText WholeConversation()
    {
        lock (_gate)
        {
            if (_turns.Count == 0) { return ChatCopyText.Nothing; }

            var builder = new StringBuilder();
            var isComplete = true;

            foreach (var turn in _turns)
            {
                if (builder.Length > 0) { builder.Append(TurnSeparator); }

                builder.Append(YouLabel).Append('\n').Append(turn.Prompt).Append(TurnSeparator)
                    .Append(ModelLabel).Append('\n').Append(turn.Answer);

                isComplete = isComplete && turn.IsComplete;
            }

            return new ChatCopyText(builder.ToString(), _turns.Count, isComplete);
        }
    }

    /// <summary>
    /// Forgets the conversation that was being kept for <c>/copy</c>, because a new one has
    /// started: the user cleared it, gave the model other instructions, or the model was loaded
    /// again.
    /// </summary>
    internal void ForgetConversation()
    {
        lock (_gate) { _turns.Clear(); }
    }

    /// <summary>One finished turn, kept so that its words can be copied exactly as they arrived.</summary>
    private sealed class ChatTurn
    {
        internal ChatTurn(string prompt, string answer, bool isComplete)
        {
            Prompt = prompt ?? string.Empty;
            Answer = answer ?? string.Empty;
            IsComplete = isComplete;
        }

        /// <summary>Gets what the user asked, exactly as it was sent.</summary>
        internal string Prompt { get; }

        /// <summary>Gets what the model answered, exactly as it wrote it.</summary>
        internal string Answer { get; }

        /// <summary>Gets whether the turn ran to its end rather than being cancelled or cut short.</summary>
        internal bool IsComplete { get; }
    }

    /// <summary>
    /// The sentence to show a person for an exception. The two sample libraries word their own
    /// exceptions for exactly this, so those are quoted as they are; anything else is unwrapped
    /// to the innermost message, which is the one that says what actually went wrong.
    /// </summary>
    /// <param name="exception">The exception to describe.</param>
    /// <returns>One sentence, never a stack trace.</returns>
    internal static string Describe(Exception exception)
    {
        if (exception == null) { return string.Empty; }

        return exception is ModelRunningException || exception is ModelAccessException
            ? exception.Message
            : ShellSession.DeepestMessage(exception);
    }

    #endregion

    #region | What the tests use |

    /// <summary>
    /// Waits until the chat has finished everything it was given: the lines the session is
    /// running or has queued, and the background work start-up began. The held output is written
    /// before it returns, so what the sink has collected is everything there is.
    /// </summary>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when nothing is left running.</returns>
    /// <exception cref="TimeoutException">Something was still running when the time ran out.</exception>
    internal Task WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        WaitAsync(() => _session.IsBusy || !_background.IsCompleted, timeout, cancellationToken);

    /// <summary>
    /// Waits only until the line the session is running has finished, leaving background work
    /// alone - which is how a test asks something of a chat whose model is still loading.
    /// </summary>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when no line is running.</returns>
    /// <exception cref="TimeoutException">A line was still running when the time ran out.</exception>
    internal Task WaitForLineAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        WaitAsync(() => _session.IsBusy, timeout, cancellationToken);

    /// <summary>Writes whatever output is being held, so that what has been collected is all of it.</summary>
    internal void FlushOutput() => _batcher.Flush();

    private async Task WaitAsync(Func<bool> busy, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();

        while (busy())
        {
            if (deadline.Elapsed > timeout)
            {
                throw new TimeoutException("The chat was still busy after " + timeout + ".");
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        _batcher.Flush();
    }

    #endregion

    #region | Start-up |

    private string[] BuildBanner() =>
    [
        TerminalText.Plain("ModelQueryTool - a chat with " + _stager.Model.DisplayName, _highlights),
        TerminalText.Plain("Type /help for the commands.", _highlights),
        string.Empty,
    ];

    private void RegisterCommands()
    {
        _commands.Register(new HelpCommand(_commands, ChatLineInterpreter.CommandPrefix, _highlights));
        _commands.Register(new StatusCommand(this));
        _commands.Register(new CopyCommand(this));
        _commands.Register(new ThinkCommand(this));
        _commands.Register(new NewConversationCommand(this));
        _commands.Register(new SetSystemPromptCommand(this));
        _commands.Register(new SetContextSizeCommand(this));
        _commands.Register(new DownloadCommand(this));
        _commands.Register(new RemoveCommand(this));
        _commands.Register(new ExitCommand(this));
    }

    /// <summary>
    /// What happens while the first prompt is already on screen: the store is asked what it
    /// holds, the answer is written out of band, and a model that is there is loaded. NOTHING IS
    /// EVER DOWNLOADED HERE - only the user asks for that, with /download -y.
    /// </summary>
    private async Task StartUpAsync()
    {
        ModelStagingStatus status;

        try
        {
            status = await _stager.CheckAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            WriteOutOfBand(TerminalText.Failure(
                Wrap("The model folder could not be read. " + Describe(exception)), _highlights));
            return;
        }

        WriteOutOfBand(DescribeStartUp(status));

        if (!status.IsReady) { return; }

        try
        {
            await LoadModelAsync(status.ModelPath, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            WriteOutOfBand(TerminalText.Dimmed(Wrap("The load was stopped; nothing is loaded."), _highlights));
            return;
        }
        catch (Exception exception)
        {
            WriteOutOfBand(TerminalText.Failure(Wrap(Describe(exception)), _highlights));
            return;
        }

        WriteOutOfBand(TerminalText.Dimmed(Wrap(
            "The model is loaded and ready - ask it something, or type /help for the commands."), _highlights));
    }

    /// <summary>The one or two dimmed lines that say what the store holds and what to do about it.</summary>
    private string[] DescribeStartUp(ModelStagingStatus status)
    {
        switch (status.State)
        {
            case ModelStagingState.Ready:
                return
                [
                    TerminalText.Dimmed(Wrap(status.Model.DisplayName + " is on disk ("
                        + ByteSize.Describe(status.BytesOnDisk) + "). Loading it now - the prompt works meanwhile."),
                        _highlights),
                ];

            case ModelStagingState.Incomplete:
                return
                [
                    TerminalText.Dimmed(Wrap("A download of " + status.Model.DisplayName
                        + " was interrupted: " + status.Detail), _highlights),
                    TerminalText.Dimmed(Wrap("Type /download -y to carry on from where it stopped."), _highlights),
                ];

            case ModelStagingState.Damaged:
                return
                [
                    TerminalText.Dimmed(Wrap(status.Model.DisplayName + " is on disk but is not usable: "
                        + status.Detail), _highlights),
                    TerminalText.Dimmed(Wrap(
                        "Type /download -y to repair it; if that does not help, /remove -y and then /download -y."),
                        _highlights),
                ];

            default:
                return
                [
                    TerminalText.Dimmed(Wrap(status.Model.DisplayName + " has not been downloaded yet."), _highlights),
                    TerminalText.Dimmed(Wrap(
                        "Type /download to see what that would involve, or /download -y to start it."), _highlights),
                ];
        }
    }

    #endregion

    #region | A turn |

    /// <summary>
    /// What an ordinary line means: a message to the model. It is sent verbatim - the quotes,
    /// the backslashes and the row breaks a paste left in it are all part of the question.
    /// </summary>
    private async Task HandleChatLineAsync(ShellSession session, string line, CancellationToken cancellationToken)
    {
        if (IsShutDown)
        {
            WriteNotice("The model has been unloaded and the application is closing.");

            return;
        }

        switch (Busy)
        {
            case ChatBusyKind.Downloading:
                WriteNotice("A download is running; the model cannot answer until it has finished.");
                return;

            case ChatBusyKind.Loading:
                WriteNotice("The model is still loading; ask again in a moment.");
                return;
        }

        if (_host.State != ModelHostState.Ready)
        {
            WriteNotice("No model is loaded. /status says what is on disk, and /download -y obtains it.");
            return;
        }

        _wrapper.Columns = _columns;
        _wrapper.Reset(0);
        _thinkingOpen = false;
        var reminded = false;

        //The answer as the MODEL writes it, kept beside the answer as the terminal shows it: the
        //  pieces joined, nothing wrapped, no reasoning and no escape sequence. /copy hands this
        //  to the clipboard.
        var answer = new StringBuilder();
        var completed = false;

        ShowTurnWait();

        try
        {
            await foreach (var update in _host.SendAsync(line, cancellationToken).ConfigureAwait(false))
            {
                switch (update.Kind)
                {
                    case ChatTurnUpdateKind.Notice:
                        CloseThinking();
                        WriteNotice(update.Text);
                        break;

                    case ChatTurnUpdateKind.Thinking:
                        HideTurnWait();
                        if (!_thinkingOpen)
                        {
                            if (!reminded)
                            {
                                WriteNotice(ThinkingReminder);
                                reminded = true;
                            }

                            Write(TerminalText.DimOn);
                            _wrapper.Reset(0);
                            _thinkingOpen = true;
                        }

                        Write(_wrapper.Write(update.Text));
                        break;

                    case ChatTurnUpdateKind.Content:
                        HideTurnWait();
                        if (_thinkingOpen)
                        {
                            CloseThinking();

                            //A blank row between the reasoning and the answer, so the two do not
                            //  read as one paragraph.
                            IO.WriteLine();
                            _wrapper.Reset(0);
                        }

                        answer.Append(update.Text);
                        Write(_wrapper.Write(update.Text));
                        break;

                    case ChatTurnUpdateKind.Completed:
                        CloseAnswer();
                        Remember(update);
                        completed = true;
                        WriteFinishNote(update.FinishReason);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            //The partial answer stays on screen; the session writes the ^C.
            CloseAnswer();
            throw;
        }
        catch (Exception exception)
        {
            CloseAnswer();
            WriteError(Describe(exception));
        }
        finally
        {
            HideTurnWait();
            RememberTurn(line, answer.ToString(), completed);
        }
    }

    /// <summary>
    /// Keeps a turn that produced words, so that <c>/copy</c> can hand them over exactly as they
    /// arrived. A turn that produced nothing at all - one cancelled before the model's first word
    /// - is not a turn anybody would want to copy, so it is not kept.
    /// </summary>
    /// <param name="prompt">What the user asked, exactly as it was sent.</param>
    /// <param name="answer">What the model answered, the pieces joined.</param>
    /// <param name="isComplete">Whether the turn ran to its end.</param>
    private void RememberTurn(string prompt, string answer, bool isComplete)
    {
        if (answer.Length == 0) { return; }

        lock (_gate) { _turns.Add(new ChatTurn(prompt, answer, isComplete)); }
    }

    /// <summary>
    /// Shows the status bar for the wait every turn opens with. The engine reads the whole
    /// conversation again before the first word of an answer appears, which on a long
    /// conversation is a wait of many seconds with nothing on screen - so it is reported, and
    /// the Cancel button is live throughout it.
    /// </summary>
    private void ShowTurnWait()
    {
        var tokens = ConversationTokensOrZero();
        _turnWaitShown = true;
        _shellHost.ShowProgress(
            tokens > 0
                ? "Reading the conversation (about " + tokens.ToString("N0", CultureInfo.InvariantCulture)
                    + " tokens)..."
                : "Reading the conversation...",
            0d,
            true);
    }

    /// <summary>Hides the turn's status bar - at the first text of the answer, and however else the turn ends.</summary>
    private void HideTurnWait()
    {
        if (!_turnWaitShown) { return; }

        _turnWaitShown = false;
        _shellHost.HideProgress();
    }

    private int ConversationTokensOrZero()
    {
        try
        {
            return _host.ConversationTokens;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    /// <summary>Ends a dimmed reasoning block: what is held is written, the dim is reset, the row ends.</summary>
    private void CloseThinking()
    {
        if (!_thinkingOpen) { return; }

        Write(_wrapper.Flush());
        Write(TerminalText.DimOff);
        _thinkingOpen = false;
        EndRow();
    }

    /// <summary>Ends whatever the model was writing: the held word, any dim still on, and the row.</summary>
    private void CloseAnswer()
    {
        if (_thinkingOpen)
        {
            CloseThinking();
            return;
        }

        Write(_wrapper.Flush());
        EndRow();
    }

    private void EndRow()
    {
        if (_wrapper.Column > 0) { IO.WriteLine(); }
    }

    private void Remember(ChatTurnUpdate update)
    {
        lock (_gate)
        {
            _lastStatistics = update.Statistics;
            _lastFinishReason = update.FinishReason;
        }
    }

    private void WriteFinishNote(FinishReason reason)
    {
        switch (reason)
        {
            case FinishReason.ContextFull:
                WriteNotice("The answer stopped because the context filled up. /clear starts a new conversation, "
                    + "and /set-context-size -y <tokens> makes more room.");
                break;

            case FinishReason.Length:
                WriteNotice("The answer stopped because it reached the length the request allowed.");
                break;
        }
    }

    #endregion

    #region | Long work, and the status bar that reports it |

    private CancellationTokenSource BeginOperation(ChatBusyKind kind, CancellationToken cancellationToken)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        lock (_gate)
        {
            _busy = kind;
            _operation = operation;
        }

        return operation;
    }

    private void EndOperation()
    {
        lock (_gate)
        {
            _busy = ChatBusyKind.None;
            _operation = null;
        }

        _shellHost.HideProgress();
    }

    /// <summary>
    /// Puts the load on the status bar and hands back its progress reporter. The engine calls
    /// the reporter on its own loading thread, often, so only a change of whole percent is
    /// passed on.
    /// </summary>
    private IProgress<float> BeginLoadProgress(string verb)
    {
        var caption = verb + _stager.Model.DisplayName + "...";
        var lastPercent = -1;
        _shellHost.ShowProgress(caption, 0d, false);

        return new DelegateProgress<float>(fraction =>
        {
            var percent = (int)Math.Round(Math.Clamp(fraction, 0f, 1f) * 100d);
            if (percent == lastPercent) { return; }

            lastPercent = percent;
            _shellHost.ShowProgress(caption, percent, false);
        });
    }

    private async Task ShutdownCoreAsync()
    {
        CancellationTokenSource operation;

        lock (_gate)
        {
            operation = _operation;
        }

        if (operation != null)
        {
            try
            {
                operation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                //The work finished between reading its token source and cancelling it.
            }
        }

        _lifetime.Cancel();

        try
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            //Closing down never fails: whatever the model could not do, the process is going anyway.
        }

        _shellHost.HideProgress();
        _batcher.Flush();
    }

    #endregion

    /// <summary>Writes lines that arrive while the user may be half way through typing one of their own.</summary>
    private void WriteOutOfBand(params string[] lines) =>
        _shellHost.Post(() => _session.WriteOutOfBand(lines));

    private void Write(string text) => IO.Write(text);
}
