using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The one thread every native call for a loaded model is made on: a work queue drained by a single
/// long-lived thread.
/// </summary>
/// <remarks>
/// <para>
/// A <c>llama_context</c> is not re-entrant. Two decodes in flight on one context corrupt its memory, and
/// the engine offers no lock of its own, so the binding has to provide one. A dedicated thread is used
/// rather than a semaphore plus <see cref="Task.Run(Action)"/> because it gives something a semaphore does
/// not: every call for one model is made from the same OS thread for the model's whole life, which is what
/// the engine's own examples do and what keeps thread-affine backend state - and any thread-local state a
/// backend keeps - in one place.
/// </para>
/// <para>
/// Work items never throw out of the pump: each one is wrapped so the result or the exception lands on the
/// caller's task instead. Continuations are forced off this thread so that awaiting a work item's result
/// can never run caller code on the engine thread.
/// </para>
/// </remarks>
internal sealed class EngineWorker : IDisposable
{
    private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
    private readonly Thread thread;
    private bool disposed;

    /// <summary>Starts the worker thread.</summary>
    /// <param name="name">The thread's name, which shows up in a debugger and in a stack dump.</param>
    public EngineWorker(string name)
    {
        thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = string.IsNullOrEmpty(name) ? "CodeBrix.Ollama engine" : name,
        };

        thread.Start();
    }

    /// <summary>Whether the calling thread is this worker's thread.</summary>
    public bool IsWorkerThread => ReferenceEquals(Thread.CurrentThread, thread);

    /// <summary>Queues work that produces a value and returns a task for its result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">The work, which runs on the worker thread.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    public Task<T> RunAsync<T>(Func<T> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));

        TaskCompletionSource<T> completion =
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Work posted from the worker itself would deadlock waiting for a pump that is busy running it, so
        // it is run inline. Nothing in the engine does this, but a callback that reaches back in might.
        if (IsWorkerThread)
        {
            Complete(completion, work);
            return completion.Task;
        }

        try
        {
            queue.Add(() => Complete(completion, work));
        }
        catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException)
        {
            // BlockingCollection answers the first way once adding has been completed and the second once
            // the collection itself is gone, and Dispose does both: the model is going away and the work
            // will never run.
            completion.TrySetException(new ObjectDisposedException(nameof(EngineWorker)));
        }

        return completion.Task;
    }

    /// <summary>Queues work that produces no value.</summary>
    /// <param name="work">The work, which runs on the worker thread.</param>
    /// <returns>A task that completes when the work has run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    public Task RunAsync(Action work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));

        return RunAsync<bool>(() =>
        {
            work();
            return true;
        });
    }

    /// <summary>Stops the worker and waits for the thread to finish, at most half a minute.</summary>
    /// <remarks>
    /// Adding is completed rather than the queue being torn down, so the pump drains whatever is already
    /// queued and then ends of its own accord. The queue is freed only once the thread has really finished:
    /// freeing it under a pump that is still blocked in <c>GetConsumingEnumerable</c> - which is what a join
    /// that timed out means - would throw on the engine thread, where nothing can catch it on the caller's
    /// behalf.
    /// </remarks>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        queue.CompleteAdding();

        if (IsWorkerThread) return;
        if (!thread.Join(TimeSpan.FromSeconds(30))) return;

        queue.Dispose();
    }

    private static void Complete<T>(TaskCompletionSource<T> completion, Func<T> work)
    {
        try
        {
            completion.TrySetResult(work());
        }
        catch (OperationCanceledException canceled)
        {
            completion.TrySetCanceled(canceled.CancellationToken);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private void Pump()
    {
        try
        {
            foreach (Action work in queue.GetConsumingEnumerable())
            {
                work();
            }
        }
        catch (Exception)
        {
            // Nothing may leave this thread. A work item's own failure is already landed on the caller's
            // task by Complete, so the only way here is the queue ending underneath the pump, and an
            // exception escaping a thread would take the whole process down with it.
        }
    }
}
