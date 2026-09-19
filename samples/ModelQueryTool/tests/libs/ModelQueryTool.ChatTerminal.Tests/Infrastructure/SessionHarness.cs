using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;

namespace ModelQueryTool.ChatTerminal.Tests.Infrastructure;

/// <summary>
/// A whole <see cref="ShellSession"/> wired to a real headless terminal the way
/// the application wires it to the control - the session's output goes straight
/// into <c>Feed</c> - so that an assertion can be about the SCREEN and the
/// CURSOR. That is the only way to say that an out-of-band message left nothing
/// stale behind: the bytes alone cannot tell a prompt that was erased from one
/// that is still sitting there.
/// </summary>
internal sealed class SessionHarness
{
    private readonly HeadlessTerminal _terminal;
    private readonly string _prompt;

    /// <summary>Creates the harness and starts the session, which writes the first prompt.</summary>
    /// <param name="columns">The terminal's column count.</param>
    /// <param name="rows">The terminal's row count.</param>
    /// <param name="prompt">The prompt in front of the input line.</param>
    public SessionHarness(int columns, int rows = 24, string prompt = "chat> ")
    {
        _prompt = prompt;
        _terminal = new HeadlessTerminal(columns, rows);
        Interpreter = new GatedInterpreter(prompt);
        Session = new ShellSession(Interpreter);
        Session.OutputProduced += _terminal.Feed;
        Session.SetGridSize(columns, rows);
        Session.Start();
    }

    /// <summary>The session under test.</summary>
    public ShellSession Session { get; }

    /// <summary>The interpreter every submitted line reaches.</summary>
    public GatedInterpreter Interpreter { get; }

    /// <summary>The terminal the session is writing to.</summary>
    public HeadlessTerminal Terminal => _terminal;

    /// <summary>Types the characters one at a time, as a user does.</summary>
    /// <param name="text">The characters to type.</param>
    public void Type(string text)
    {
        foreach (var c in text) { Session.SendInput(c.ToString()); }
    }

    /// <summary>Sends one chunk of input exactly as the control would deliver it.</summary>
    /// <param name="data">The chunk.</param>
    public void Send(string data) => Session.SendInput(data);

    /// <summary>Presses Enter, which the control delivers in a chunk of its own.</summary>
    public void Enter() => Session.SendInput("\r");

    /// <summary>Presses the left arrow the given number of times.</summary>
    /// <param name="count">How many times.</param>
    public void MoveLeft(int count)
    {
        for (var index = 0; index < count; index++) { Session.SendInput("\x1b[D"); }
    }

    /// <summary>
    /// Asserts that the screen reads as the given rows followed by the prompt
    /// and the line being typed, and that the terminal's cursor sits where that
    /// line's cursor belongs.
    /// </summary>
    /// <param name="above">The rows above the input line, top to bottom.</param>
    /// <param name="text">The text being edited.</param>
    /// <param name="cursorIndex">The cursor's index within that text.</param>
    public void AssertScreenShows(IReadOnlyList<string> above, string text, int cursorIndex)
    {
        var (block, cursorRow, cursorColumn) =
            ScreenModel.LayOut(_prompt, text, _terminal.Columns, cursorIndex);

        var expected = new List<string>(above);
        expected.AddRange(block);

        _terminal.Screen().Should().Equal(
            expected, "the screen should read as the model lays it out. Actual:\n" + _terminal.Describe());
        _terminal.CursorRow.Should().Be(
            above.Count + cursorRow, "the cursor's row. Actual:\n" + _terminal.Describe());
        _terminal.CursorColumn.Should().Be(
            cursorColumn, "the cursor's column. Actual:\n" + _terminal.Describe());
    }
}

/// <summary>
/// A root interpreter that records the lines it is given and, when a gate is
/// set, holds the line open until the test releases it - which is how a test
/// puts the session in the "a line is running" state on purpose.
/// </summary>
internal sealed class GatedInterpreter : ILineInterpreter
{
    /// <summary>Creates the interpreter with the prompt the session writes.</summary>
    /// <param name="prompt">The prompt.</param>
    public GatedInterpreter(string prompt)
    {
        Prompt = prompt;
    }

    /// <summary>The lines that were submitted, in order.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>What a running line waits for, or null to finish at once.</summary>
    public TaskCompletionSource Gate { get; set; }

    /// <inheritdoc />
    public string Prompt { get; }

    /// <inheritdoc />
    public async Task HandleLineAsync(ShellSession session, string line, CancellationToken cancellationToken)
    {
        Lines.Add(line);

        if (Gate != null) { await Gate.Task.ConfigureAwait(false); }
    }
}
