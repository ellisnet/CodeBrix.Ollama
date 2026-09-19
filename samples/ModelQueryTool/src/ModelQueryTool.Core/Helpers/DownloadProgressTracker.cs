using System;

namespace ModelQueryTool.Helpers;

/// <summary>
/// Decides how often the download caption is rebuilt and what rate it reports. The store reports
/// progress many times a second; a caption that changed that often would be unreadable, and a
/// rate worked out between two of those reports jumps about far too much to be worth showing.
/// </summary>
/// <remarks>
/// The clock is the caller's: every report carries how long the download has been running, so a
/// test drives the tracker over a whole download without waiting for any of it.
/// </remarks>
public sealed class DownloadProgressTracker
{
    /// <summary>How much of a new measurement a new rate is made of; the rest is the old rate.</summary>
    private const double SmoothingFactor = 0.3d;

    /// <summary>How long the caption stands before it is rebuilt: twice a second.</summary>
    public static readonly TimeSpan DefaultUpdateInterval = TimeSpan.FromMilliseconds(500);

    private readonly TimeSpan _updateInterval;

    private bool _started;
    private TimeSpan _lastElapsed;
    private long _lastBytes;
    private double _bytesPerSecond;

    /// <summary>Creates a tracker.</summary>
    /// <param name="updateInterval">
    /// How long a caption stands before it is rebuilt, or a zero or negative span for
    /// <see cref="DefaultUpdateInterval"/>.
    /// </param>
    public DownloadProgressTracker(TimeSpan updateInterval = default)
    {
        _updateInterval = updateInterval > TimeSpan.Zero ? updateInterval : DefaultUpdateInterval;
    }

    /// <summary>Gets the smoothed rate in bytes per second, or zero until one has been measured.</summary>
    public double BytesPerSecond => _bytesPerSecond;

    /// <summary>
    /// Takes one report from the download and says whether the caption changed. The first report
    /// and the report that completes the download always produce a caption; the ones in between
    /// do so only once the interval has passed.
    /// </summary>
    /// <param name="completedBytes">How many bytes have arrived.</param>
    /// <param name="totalBytes">How many bytes the whole download comes to, or zero when not known.</param>
    /// <param name="elapsed">How long the download has been running.</param>
    /// <param name="caption">The new caption, or null when the old one still stands.</param>
    /// <returns>True when there is a new caption to show.</returns>
    public bool TryFormat(long completedBytes, long totalBytes, TimeSpan elapsed, out string caption)
    {
        caption = null;
        bool isComplete = totalBytes > 0L && completedBytes >= totalBytes;

        if (_started && !isComplete && elapsed - _lastElapsed < _updateInterval)
        {
            return false;
        }

        double seconds = (elapsed - _lastElapsed).TotalSeconds;
        if (_started && seconds > 0d)
        {
            double measured = Math.Max(0d, (completedBytes - _lastBytes) / seconds);
            _bytesPerSecond = _bytesPerSecond <= 0d
                ? measured
                : (_bytesPerSecond * (1d - SmoothingFactor)) + (measured * SmoothingFactor);
        }

        _started = true;
        _lastElapsed = elapsed;
        _lastBytes = completedBytes;
        caption = DownloadCaption.Format(completedBytes, totalBytes, _bytesPerSecond);

        return true;
    }
}
