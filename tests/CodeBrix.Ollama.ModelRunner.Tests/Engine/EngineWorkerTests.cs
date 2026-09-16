using System;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The one thread every native call for a model is made on: what it does with the work it is given, and
/// what shutting it down does to work that is queued, running, or offered afterwards.
/// </summary>
public sealed class EngineWorkerTests
{
    /// <summary>Every work item runs on the same thread, and never on the caller's.</summary>
    [Fact]
    public async Task RunAsync_runs_every_item_on_the_one_thread()
    {
        //Arrange
        using EngineWorker worker = new EngineWorker("test worker");

        //Act
        int first = await worker.RunAsync(() => Environment.CurrentManagedThreadId);
        int second = await worker.RunAsync(() => Environment.CurrentManagedThreadId);

        //Assert
        first.Should().Be(second);
        (first == Environment.CurrentManagedThreadId).Should().BeFalse();
    }

    /// <summary>A work item's failure lands on the caller's task rather than on the worker thread.</summary>
    [Fact]
    public async Task RunAsync_lands_a_failure_on_the_callers_task()
    {
        //Arrange
        using EngineWorker worker = new EngineWorker("test worker");

        //Act
        Func<Task> failing = () => worker.RunAsync<int>(() => throw new InvalidTimeZoneException("no"));

        //Assert
        await failing.Should().ThrowAsync<InvalidTimeZoneException>();

        // The thread survived it and is still taking work.
        (await worker.RunAsync(() => 7)).Should().Be(7);
    }

    /// <summary>Work already queued when the worker is shut down still runs, because adding is completed.</summary>
    [Fact]
    public void Dispose_lets_the_queued_work_drain()
    {
        //Arrange
        EngineWorker worker = new EngineWorker("test worker");
        using ManualResetEventSlim held = new ManualResetEventSlim(false);
        int ran = 0;

        Task blocking = worker.RunAsync(() => held.Wait(TimeSpan.FromSeconds(10)));
        Task queued = worker.RunAsync(() => Interlocked.Increment(ref ran));

        //Act
        held.Set();
        worker.Dispose();

        //Assert
        blocking.IsCompleted.Should().BeTrue();
        queued.IsCompleted.Should().BeTrue();
        Volatile.Read(ref ran).Should().Be(1);
    }

    /// <summary>Work offered after the shutdown is refused on its task, and disposing twice is harmless.</summary>
    [Fact]
    public async Task RunAsync_after_Dispose_refuses_on_the_task()
    {
        //Arrange
        EngineWorker worker = new EngineWorker("test worker");
        worker.Dispose();
        worker.Dispose();

        //Act
        Task<int> refused = worker.RunAsync(() => 1);

        //Assert
        refused.Should().NotBeNull();
        Func<Task> awaiting = () => refused;
        await awaiting.Should().ThrowAsync<ObjectDisposedException>();
    }
}
