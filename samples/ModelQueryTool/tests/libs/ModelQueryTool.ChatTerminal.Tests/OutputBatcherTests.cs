using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Output;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="OutputBatcher"/>.</summary>
public class OutputBatcherTests
{
    /// <summary>A batcher whose clock the test turns, so nothing waits on real time.</summary>
    private static OutputBatcher CreateManual(List<string> batches) =>
        new(batches.Add, TimeSpan.FromMilliseconds(50), useTimer: false);

    [Fact]
    public void nothing_is_written_until_the_interval_elapses()
    {
        //Arrange
        var batches = new List<string>();
        using var batcher = CreateManual(batches);

        //Act
        batcher.Append("one");
        batcher.Append("two");

        //Assert
        batches.Should().BeEmpty();
    }

    [Fact]
    public void a_tick_writes_everything_held_as_one_batch()
    {
        //Arrange
        var batches = new List<string>();
        using var batcher = CreateManual(batches);
        batcher.Append("one");
        batcher.Append("two");
        batcher.Append("three");

        //Act
        batcher.Tick();

        //Assert
        batches.Should().Equal("onetwothree");
    }

    [Fact]
    public void flush_writes_immediately_and_in_order()
    {
        //Arrange
        var batches = new List<string>();
        using var batcher = CreateManual(batches);

        //Act
        batcher.Append("first ");
        batcher.Flush();
        batcher.Append("second");
        batcher.Flush();

        //Assert
        batches.Should().Equal("first ", "second");
    }

    [Fact]
    public void an_empty_batcher_writes_nothing_when_it_is_ticked()
    {
        //Arrange
        var batches = new List<string>();
        using var batcher = CreateManual(batches);

        //Act
        batcher.Tick();
        batcher.Flush();

        //Assert
        batches.Should().BeEmpty();
    }

    [Fact]
    public void the_timer_is_scheduled_by_the_first_append_and_stops_after_a_flush()
    {
        //Arrange
        var batches = new List<string>();
        using var batcher = CreateManual(batches);

        //Assert - idle
        batcher.IsScheduled.Should().BeFalse();

        //Act
        batcher.Append("text");

        //Assert - scheduled
        batcher.IsScheduled.Should().BeTrue();

        //Act
        batcher.Flush();

        //Assert - idle again, so an empty batcher never fires
        batcher.IsScheduled.Should().BeFalse();
    }

    [Fact]
    public void empty_and_null_text_is_ignored()
    {
        //Arrange
        var batches = new List<string>();
        using var batcher = CreateManual(batches);

        //Act
        batcher.Append(null);
        batcher.Append(string.Empty);

        //Assert
        batcher.IsScheduled.Should().BeFalse();
        batcher.Flush();
        batches.Should().BeEmpty();
    }

    [Fact]
    public void disposing_writes_what_is_held()
    {
        //Arrange
        var batches = new List<string>();
        var batcher = CreateManual(batches);
        batcher.Append("last words");

        //Act
        batcher.Dispose();

        //Assert
        batches.Should().Equal("last words");
    }

    [Fact]
    public void disposing_twice_writes_once()
    {
        //Arrange
        var batches = new List<string>();
        var batcher = CreateManual(batches);
        batcher.Append("once");

        //Act
        batcher.Dispose();
        batcher.Dispose();

        //Assert
        batches.Should().Equal("once");
    }

    [Fact]
    public void appending_after_disposal_is_an_error()
    {
        //Arrange
        var batcher = CreateManual([]);
        batcher.Dispose();

        //Act
        Action act = () => batcher.Append("too late");

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void the_sink_must_not_be_null()
    {
        //Act
        Action act = () => new OutputBatcher(null, TimeSpan.FromMilliseconds(50));

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_interval_must_not_be_negative()
    {
        //Act
        Action act = () => new OutputBatcher(_ => { }, TimeSpan.FromMilliseconds(-1));

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task appending_from_many_threads_loses_nothing()
    {
        //Arrange
        var batches = new List<string>();
        var gate = new object();
        using var batcher = new OutputBatcher(
            text => { lock (gate) { batches.Add(text); } },
            TimeSpan.FromMilliseconds(50),
            useTimer: false);

        //Act - eight writers and a flusher, all at once
        var token = TestContext.Current.CancellationToken;
        var workers = new List<Task>();
        for (var w = 0; w < 8; w++)
        {
            workers.Add(Task.Run(() =>
            {
                for (var i = 0; i < 200; i++) { batcher.Append("x"); }
            }, token));
        }

        workers.Add(Task.Run(() =>
        {
            for (var i = 0; i < 100; i++) { batcher.Flush(); }
        }, token));

        await Task.WhenAll(workers);
        batcher.Flush();

        //Assert - every single character arrived
        string.Concat(batches).Length.Should().Be(8 * 200);
    }

    [Fact]
    public void the_order_of_what_one_thread_appends_is_never_changed()
    {
        //Arrange
        var batches = new List<string>();
        var gate = new object();
        using var batcher = new OutputBatcher(
            text => { lock (gate) { batches.Add(text); } },
            TimeSpan.FromMilliseconds(1),
            useTimer: false);

        //Act
        for (var i = 0; i < 500; i++)
        {
            batcher.Append(i.ToString() + ";");
            if (i % 7 == 0) { batcher.Flush(); }
        }

        batcher.Flush();

        //Assert
        var expected = string.Empty;
        for (var i = 0; i < 500; i++) { expected += i + ";"; }

        string.Concat(batches).Should().Be(expected);
    }

    [Fact]
    public async Task the_real_timer_writes_the_batch_by_itself()
    {
        //Arrange
        var written = new TaskCompletionSource<string>();
        using var batcher = new OutputBatcher(text => written.TrySetResult(text), TimeSpan.FromMilliseconds(20));

        //Act
        batcher.Append("from the timer");
        var finished = await Task.WhenAny(written.Task, Task.Delay(TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken));

        //Assert
        finished.Should().BeSameAs(written.Task);
        (await written.Task).Should().Be("from the timer");
        batcher.IsScheduled.Should().BeFalse();
    }
}
