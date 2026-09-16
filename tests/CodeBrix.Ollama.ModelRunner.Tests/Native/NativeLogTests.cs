using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers routing the engine's log output to a managed handler: whole lines arrive during a model load, and
/// clearing the handler stops them.
/// </summary>
public sealed class NativeLogTests : IDisposable
{
    /// <summary>Clears the handler after each test, whatever the test did with it.</summary>
    public void Dispose()
    {
        ModelRunner.SetLogHandler(null);
    }

    /// <summary>A handler receives at least one whole line while a model loads.</summary>
    [Fact]
    public void SetLogHandler_receives_lines_during_a_model_load()
    {
        //Arrange
        List<string> lines = new List<string>();
        object gate = new object();
        ModelRunner.SetLogHandler((level, text) =>
        {
            lock (gate) lines.Add(text);
        });

        //Act
        LoadAndFreeTheConformanceModel();

        //Assert
        lock (gate)
        {
            lines.Should().NotBeEmpty();
            foreach (string line in lines)
            {
                line.Should().NotContain("\n");
            }
        }
    }

    /// <summary>Clearing the handler stops the lines.</summary>
    [Fact]
    public void SetLogHandler_with_null_discards_the_lines()
    {
        //Arrange
        int count = 0;
        ModelRunner.SetLogHandler((level, text) => count++);
        LoadAndFreeTheConformanceModel();
        count.Should().NotBe(0);

        //Act
        ModelRunner.SetLogHandler(null);
        count = 0;
        LoadAndFreeTheConformanceModel();

        //Assert
        count.Should().Be(0);
    }

    /// <summary>Partial lines are held back until the newline arrives.</summary>
    [Fact]
    public void Append_holds_a_partial_line_until_its_newline()
    {
        //Arrange
        ModelRunner.SetLogHandler(null);

        //Act
        string[] none = NativeLog.Append("half a ");
        string[] one = NativeLog.Append("line\n");
        string[] two = NativeLog.Append("a\r\nb\n");

        //Assert
        none.Should().BeNull();
        one.Should().HaveCount(1);
        one[0].Should().Be("half a line");
        two.Should().HaveCount(2);
        two[0].Should().Be("a");
        two[1].Should().Be("b");
    }

    /// <summary>An unfinished line that passes the cap is handed on rather than buffered for ever.</summary>
    [Fact]
    public void Append_flushes_a_pending_line_that_passes_the_cap()
    {
        //Arrange
        ModelRunner.SetLogHandler(null);

        //Act
        string[] flushed = NativeLog.Append(new string('x', NativeLog.MaxPendingLineLength + 16));
        string[] after = NativeLog.Append("tail\n");

        //Assert
        flushed.Should().NotBeNull();
        flushed.Should().HaveCount(1);
        flushed[0].Length.Should().Be(NativeLog.MaxPendingLineLength + 16);
        after.Should().HaveCount(1);
        after[0].Should().Be("tail");
    }

    /// <summary>A line keeps the severity it completed with, not the one a later statement set.</summary>
    [Fact]
    public async Task Dispatch_keeps_the_severity_a_line_completed_with()
    {
        //Arrange
        ModelRunner.SetLogHandler(null);
        object gate = new object();
        List<ModelRunnerLogLevel> levels = new List<ModelRunnerLogLevel>();
        using ManualResetEventSlim firstLineSeen = new ManualResetEventSlim(false);
        using ManualResetEventSlim severityChanged = new ManualResetEventSlim(false);

        ModelRunner.SetLogHandler((level, text) =>
        {
            if (!text.StartsWith("severity-test-", StringComparison.Ordinal)) return;

            lock (gate) levels.Add(level);

            firstLineSeen.Set();
            severityChanged.Wait(TimeSpan.FromSeconds(10));
        });

        //Act
        Task later = Task.Factory.StartNew(
            () =>
            {
                firstLineSeen.Wait(TimeSpan.FromSeconds(10));
                NativeLog.Dispatch(GgmlLogLevel.Debug, "a later statement, still without its newline");
                severityChanged.Set();
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        NativeLog.Dispatch(GgmlLogLevel.Error, "severity-test-one\nseverity-test-two\n");
        await later;

        //Assert
        lock (gate)
        {
            levels.Should().HaveCount(2);
            levels[0].Should().Be(ModelRunnerLogLevel.Error);
            levels[1].Should().Be(ModelRunnerLogLevel.Error);
        }
    }

    private static unsafe void LoadAndFreeTheConformanceModel()
    {
        LlamaModelParams parameters = NativeDefaults.ModelParams;
        parameters.NGpuLayers = 0;
        parameters.VocabOnly = 0;

        IntPtr model = NativeMethods.llama_model_load_from_file(TestVectors.ConformanceModelPath, parameters);
        model.Should().NotBe(IntPtr.Zero);
        NativeMethods.llama_model_free(model);
    }
}
