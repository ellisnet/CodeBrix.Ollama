using System;
using System.Text;
using System.Threading;

namespace ModelQueryTool.ChatTerminal.Output;

/// <summary>
/// Collects small pieces of output and hands them to the sink in one call every
/// so often. Writing to the terminal control costs a dispatcher hop and a
/// repaint per call, so a model that produces a dozen tokens a second should not
/// buy a dozen repaints a second.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is ever lost and nothing is ever reordered: the pieces go out in the
/// order they arrived, in one string. <see cref="Flush"/> writes what is held
/// immediately, and disposing flushes. The timer is one-shot and is scheduled only
/// by an <see cref="Append"/> that finds the batcher empty, so an idle batcher
/// costs nothing.
/// </para>
/// <para>
/// THREADING. <see cref="Append"/> and <see cref="Flush"/> are safe from any
/// thread. The sink is invoked while the batcher's lock is held, which is what
/// keeps the order of two concurrent flushes honest, so the sink must return
/// promptly - the terminal control's Feed does, from any thread.
/// </para>
/// </remarks>
public sealed class OutputBatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly Action<string> _sink;
    private readonly TimeSpan _interval;
    private readonly Timer _timer;

    private bool _scheduled;
    private bool _disposed;

    /// <summary>Creates a batcher that writes to the sink at most once per interval.</summary>
    /// <param name="sink">Where a batch goes - the terminal's Feed, in the application.</param>
    /// <param name="interval">How long output is collected before it is written.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is negative.</exception>
    public OutputBatcher(Action<string> sink, TimeSpan interval)
        : this(sink, interval, useTimer: true)
    {
    }

    /// <summary>
    /// Creates a batcher whose timer the caller drives with <see cref="Tick"/>,
    /// so that a test can prove the batching without waiting for a clock.
    /// </summary>
    internal OutputBatcher(Action<string> sink, TimeSpan interval, bool useTimer)
    {
        if (sink == null) { throw new ArgumentNullException(nameof(sink)); }
        if (interval < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(interval), "An interval is not negative."); }

        _sink = sink;
        _interval = interval;
        if (useTimer)
        {
            _timer = new Timer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Whether a batch is waiting for the interval to elapse.</summary>
    internal bool IsScheduled
    {
        get { lock (_gate) { return _scheduled; } }
    }

    /// <summary>
    /// Adds text to the batch. It is written when the interval elapses, or at
    /// the next <see cref="Flush"/>, whichever comes first.
    /// </summary>
    /// <param name="text">The text to add; null or empty adds nothing.</param>
    /// <exception cref="ObjectDisposedException">The batcher has been disposed.</exception>
    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        lock (_gate)
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(OutputBatcher)); }

            _pending.Append(text);
            if (!_scheduled)
            {
                _scheduled = true;
                _timer?.Change(_interval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Writes everything held, in order, right now. Does nothing when nothing is
    /// held, and nothing after the batcher has been disposed.
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            FlushCore();
        }
    }

    /// <summary>Writes anything still held and stops the timer.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            FlushCore();
            _disposed = true;
        }

        _timer?.Dispose();
    }

    /// <summary>
    /// What the timer does when it elapses. Tests without a timer call it
    /// themselves.
    /// </summary>
    internal void Tick() => Flush();

    private void FlushCore()
    {
        _scheduled = false;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        if (_pending.Length == 0) { return; }

        var batch = _pending.ToString();
        _pending.Clear();
        _sink(batch);
    }
}
