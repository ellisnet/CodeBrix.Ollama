using ModelQueryTool.Core.Tests.Fakes;
using ModelQueryTool.Core.Tests.Infrastructure;
using ModelQueryTool.ModelAccess;
using ModelQueryTool.ModelRunning;
using ModelQueryTool.Services;
using SilverAssertions;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// The one test in this project that uses the REAL model: the real stager, the real host and the
/// real chat, over the model the application keeps in its own folder.
/// </summary>
/// <remarks>
/// <para>
/// It is behind <c>MODELQUERYTOOL_TEST_CHAT_QWEN35=1</c> and has no timeout, because loading
/// twenty gibibytes of weights and reading a conversation back is measured in tens of seconds.
/// </para>
/// <para>
/// IT DOWNLOADS NOTHING AND DELETES NOTHING. When the model is not staged it fails with a message
/// saying which test to run first; obtaining the model is that test's business and nobody else's.
/// </para>
/// </remarks>
[Collection(RealModelCollection.Name)]
public class Qwen35ChatEndToEndTests
{
    private const string Question = "Reply with exactly one word: which planet do humans live on?";
    private const string GateVariable = "MODELQUERYTOOL_TEST_CHAT_QWEN35";
    private const string StagingTestVariable = "MODELQUERYTOOL_TEST_STAGE_QWEN35";

    private static readonly TimeSpan Patience = TimeSpan.FromHours(1);

    private readonly ITestOutputHelper _output;
    private readonly StringBuilder _text = new();
    private readonly object _gate = new();

    /// <summary>Creates the test class, keeping the output helper the timings are written to.</summary>
    /// <param name="output">Where the timings go.</param>
    public Qwen35ChatEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// The whole chat against the real model: it is found on disk and loaded, it answers a
    /// question with reasoning shown, it answers the same question with reasoning off and shows
    /// none, it reports its context size, and it unloads.
    /// </summary>
    [EnvGatedFact(GateVariable)]
    public async Task the_real_model_answers_through_the_chat()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stager = new ModelStager();
        var staged = await stager.CheckAsync(cancellationToken);

        if (!staged.IsReady)
        {
            Assert.Fail("The model is not staged (" + staged + "). Run the staging test first: "
                + StagingTestVariable + "=1. This test never downloads anything.");
        }

        var host = new ModelHost();
        var shellHost = new FakeChatShellHost();

        await using (host)
        {
            using var shell = new ChatShell(stager, host, Append, shellHost);
            shell.SetGridSize(100, 40);

            //Act - start-up finds the model and loads it
            var clock = Stopwatch.StartNew();
            shell.Start();
            await shell.WaitForIdleAsync(Patience, cancellationToken);
            _output.WriteLine("load: " + Seconds(clock.Elapsed));

            //Assert
            host.State.Should().Be(ModelHostState.Ready);
            Text.Should().Contain("The model is loaded and ready");

            //Act - a question, with reasoning on, which is the default
            Clear();
            clock.Restart();
            await SubmitAsync(shell, Question, cancellationToken);
            _output.WriteLine("with reasoning: " + Seconds(clock.Elapsed) + Statistics(shell));

            //Assert - the answer is there, and the reasoning was shown dimmed before it
            //  (the reminder names a command, which is written in the highlight colour, so it is
            //  looked for with the colour changes taken out)
            var thinking = Text;
            thinking.Should().Contain("Earth");
            Strip(thinking).Should().Contain(ChatShell.ThinkingReminder);
            thinking.IndexOf("\x1b[2m", StringComparison.Ordinal)
                .Should().BeLessThan(thinking.IndexOf("Earth", StringComparison.Ordinal));

            //Act - the answer goes to the clipboard as the model wrote it
            Clear();
            await SubmitAsync(shell, "/copy", cancellationToken);

            //Assert - the head was handed the model's own words: the answer, with no escape
            //  sequence and none of the reasoning in it
            shellHost.LastCopied.Should().NotBeNull();
            shellHost.LastCopied.Should().Contain("Earth");
            shellHost.LastCopied.Should().NotContain("\x1b");
            Text.Should().Contain("is on the clipboard");

            //Act - the same question with reasoning off
            Clear();
            await SubmitAsync(shell, "/think off", cancellationToken);
            Clear();
            clock.Restart();
            await SubmitAsync(shell, Question, cancellationToken);
            _output.WriteLine("without reasoning: " + Seconds(clock.Elapsed) + Statistics(shell));

            //Assert - the answer is there and nothing was dimmed on the way to it
            var direct = Text;
            direct.Should().Contain("Earth");
            Strip(direct).Should().NotContain(ChatShell.ThinkingReminder);

            //Act - the status reports the context size
            Clear();
            await SubmitAsync(shell, "/status", cancellationToken);

            //Assert
            Text.Should().Contain(host.ContextSize.ToString("N0", CultureInfo.InvariantCulture) + " tokens");

            //Act - leaving unloads the model
            await SubmitAsync(shell, "/exit", cancellationToken);

            //Assert
            shellHost.ExitCalls.Should().Be(1);
        }

        //Assert - and nothing was obtained or deleted along the way
        var afterwards = await stager.CheckAsync(cancellationToken);
        afterwards.IsReady.Should().Be(true);
    }

    private async Task SubmitAsync(ChatShell shell, string line, CancellationToken cancellationToken)
    {
        shell.SendInput(line);
        shell.SendInput("\r");
        await shell.WaitForIdleAsync(Patience, cancellationToken);
    }

    private string Text
    {
        get { lock (_gate) { return _text.ToString(); } }
    }

    private void Clear()
    {
        lock (_gate) { _text.Clear(); }
    }

    private void Append(string text)
    {
        lock (_gate) { _text.Append(text); }
    }

    /// <summary>What a reader of the terminal sees, with the colour changes taken out.</summary>
    private static string Strip(string text) =>
        Regex.Replace(text, "\x1b\\[[0-9;]*m", string.Empty);

    private static string Seconds(TimeSpan elapsed) =>
        elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private static string Statistics(ChatShell shell)
    {
        var statistics = shell.LastStatistics;

        if (statistics == null) { return " (the model reported no statistics)"; }

        return " - prompt " + statistics.PromptTokens.ToString("N0", CultureInfo.InvariantCulture)
            + " tokens (" + statistics.CachedPromptTokens.ToString("N0", CultureInfo.InvariantCulture)
            + " cached) in " + Seconds(statistics.PromptDuration)
            + ", answer " + statistics.GeneratedTokens.ToString("N0", CultureInfo.InvariantCulture)
            + " tokens at " + statistics.TokensPerSecond.ToString("0.0", CultureInfo.InvariantCulture)
            + " a second";
    }
}
