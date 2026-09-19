using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.Core.Tests.Fakes;
using ModelQueryTool.Services;

namespace ModelQueryTool.Core.Tests.Infrastructure;

/// <summary>
/// A whole chat with fakes behind it and a string builder in front of it: the test types what a
/// user would type and reads what a terminal would have shown.
/// </summary>
internal sealed class ChatShellHarness : IDisposable
{
    /// <summary>How long a test waits for the chat to go quiet before it calls that a failure.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly StringBuilder _output = new();
    private readonly object _gate = new();
    private readonly int _columns;
    private readonly int _rows;

    /// <summary>Builds the chat over its fakes.</summary>
    /// <param name="columns">How wide the terminal is.</param>
    /// <param name="rows">How tall the terminal is.</param>
    internal ChatShellHarness(int columns = 80, int rows = 24)
    {
        _columns = columns;
        _rows = rows;
        Stager = new FakeModelStager();
        Host = new FakeModelHost();
        ShellHost = new FakeChatShellHost();
        Shell = new ChatShell(Stager, Host, Append, ShellHost);
    }

    /// <summary>Gets the model store the chat sees.</summary>
    internal FakeModelStager Stager { get; }

    /// <summary>Gets the model host the chat sees.</summary>
    internal FakeModelHost Host { get; }

    /// <summary>Gets the status bar and the way out the chat sees.</summary>
    internal FakeChatShellHost ShellHost { get; }

    /// <summary>Gets the chat under test.</summary>
    internal ChatShell Shell { get; }

    /// <summary>Gets everything the chat has written to its terminal.</summary>
    internal string Text
    {
        get { lock (_gate) { return _output.ToString(); } }
    }

    /// <summary>
    /// Gets what a reader of the terminal sees: everything written, with the colour changes taken
    /// out - which is how a sentence broken up by a highlight is read as one sentence again.
    /// </summary>
    internal string PlainText => Regex.Replace(Text, "\x1b\\[[0-9;]*m", string.Empty);

    /// <summary>
    /// Gets the rows a terminal would be showing after everything the chat has written - the
    /// escape sequences applied rather than merely taken out, so that a prompt a wind-back erased
    /// is no longer there to be counted.
    /// </summary>
    internal string ScreenText => TranscriptScreen.Render(Text);

    /// <summary>Starts the chat as the page does, and waits until start-up has finished.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the chat is quiet again.</returns>
    internal Task StartAsync(CancellationToken cancellationToken)
    {
        Shell.SetGridSize(_columns, _rows);
        Shell.Start();

        return WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Types a line and presses Enter, the way the control delivers them: the text in one chunk,
    /// the line ending in another.
    /// </summary>
    /// <param name="line">What the user types.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the chat is quiet again.</returns>
    internal Task SubmitAsync(string line, CancellationToken cancellationToken)
    {
        Shell.SendInput(line);
        Shell.SendInput("\r");

        return WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Types a line and presses Enter, and waits only for that line - for a chat whose model is
    /// still loading in the background, which a wait for everything would never outlast.
    /// </summary>
    /// <param name="line">What the user types.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the line has been dealt with.</returns>
    internal Task SubmitWhileBusyAsync(string line, CancellationToken cancellationToken)
    {
        Shell.SendInput(line);
        Shell.SendInput("\r");

        return Shell.WaitForLineAsync(Patience, cancellationToken);
    }

    /// <summary>Starts the chat without waiting for the background work start-up begins.</summary>
    internal void StartWithoutWaiting()
    {
        Shell.SetGridSize(_columns, _rows);
        Shell.Start();
    }

    /// <summary>Waits until nothing is running and every held byte has been written.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the chat is quiet again.</returns>
    internal Task WaitAsync(CancellationToken cancellationToken) =>
        Shell.WaitForIdleAsync(Patience, cancellationToken);

    /// <summary>Waits for something the chat is about to do, rather than for it to stop doing things.</summary>
    /// <param name="condition">What the test is waiting for.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="TimeoutException">The condition never held.</exception>
    internal async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();

        while (!condition())
        {
            if (clock.Elapsed > Patience)
            {
                throw new TimeoutException("The chat never reached what the test was waiting for.");
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        Shell.FlushOutput();
    }

    /// <summary>Forgets everything written so far, so the next assertion reads one exchange alone.</summary>
    internal void ClearText()
    {
        lock (_gate) { _output.Clear(); }
    }

    /// <summary>
    /// Sets every "how many times" count on both fakes back to zero, so that an assertion about
    /// one command is not counting what the arrangement before it did.
    /// </summary>
    internal void ResetCounts()
    {
        Stager.ResetCounts();
        Host.ResetCounts();
    }

    /// <inheritdoc />
    public void Dispose() => Shell.Dispose();

    private void Append(string text)
    {
        lock (_gate) { _output.Append(text); }
    }
}
