using System;
using System.Threading;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The tap on the native engine's log: one operation's lines are its own, so that a second model's load or
/// decode cannot wipe the lines the first one is about to put in an exception.
/// </summary>
/// <remarks>
/// Lines are fed through the handler the tap installs rather than by really loading anything, which is what
/// the engine does to it and is the only way to drive it without a model.
/// </remarks>
public sealed class EngineLogTests
{
    /// <summary>The lines one operation records are the ones its tail reports.</summary>
    [Fact]
    public void Tail_reports_the_lines_of_this_operation()
    {
        //Arrange
        EngineLog.Arm();
        EngineLog.Clear();

        //Act
        Feed("first line");
        Feed("second line");

        //Assert
        string tail = EngineLog.Tail();
        tail.Should().Contain("first line");
        tail.Should().Contain("second line");
        EngineLog.Describe("what the binding knows").Should().Contain("what the binding knows");

        EngineLog.Clear();
        EngineLog.Tail().Should().BeEmpty();
    }

    /// <summary>One operation's lines are never wiped, nor read, by another operation running beside it.</summary>
    /// <remarks>
    /// Two real threads, because an operation is a thread: each native call a model makes is made on that
    /// model's own worker thread, and a probe runs the whole of its work on a thread of its own.
    /// </remarks>
    [Fact]
    public void Tail_keeps_two_operations_apart()
    {
        //Arrange
        EngineLog.Arm();

        using ManualResetEventSlim firstRecorded = new ManualResetEventSlim(false);
        using ManualResetEventSlim secondRecorded = new ManualResetEventSlim(false);

        string first = null;
        string second = null;

        Thread one = new Thread(() =>
        {
            EngineLog.Clear();
            Feed("the first operation's line");
            firstRecorded.Set();
            secondRecorded.Wait(TimeSpan.FromSeconds(10));
            first = EngineLog.Tail();
        });

        Thread two = new Thread(() =>
        {
            EngineLog.Clear();
            Feed("the second operation's line");
            secondRecorded.Set();
            firstRecorded.Wait(TimeSpan.FromSeconds(10));
            second = EngineLog.Tail();
        });

        //Act
        one.Start();
        two.Start();
        one.Join();
        two.Join();

        //Assert
        first.Should().Contain("the first operation's line");
        second.Should().Contain("the second operation's line");
        first.Should().NotContain("the second operation's line");
        second.Should().NotContain("the first operation's line");
    }

    private static void Feed(string line)
    {
        Action<ModelRunnerLogLevel, string> tap = NativeLog.Handler;
        tap.Should().NotBeNull();
        tap(ModelRunnerLogLevel.Info, line);
    }
}
