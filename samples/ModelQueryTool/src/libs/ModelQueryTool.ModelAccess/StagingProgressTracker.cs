using System;
using System.Collections.Generic;
using CodeBrix.Ollama.ModelManager;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.ModelAccess;

/// <summary>
/// Turns the store's per-layer reports into the ONE fraction across the whole model that a progress bar
/// is driven from.
/// </summary>
/// <remarks>
/// <para>
/// The numerator is the sum of what every layer has of itself so far. The denominator is the size the
/// descriptor expects while that is the larger number, and the sum of the layer sizes the manifest
/// turned out to state once it is not - so the bar is honest before the manifest arrives and honest
/// afterwards.
/// </para>
/// <para>
/// The percentage is held to what it has already reached, so that a denominator which grows part way
/// through cannot make a bar run backwards.
/// </para>
/// </remarks>
internal sealed class StagingProgressTracker
{
    private readonly Dictionary<string, long> _layerTotals = new Dictionary<string, long>(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _layerCompleted = new Dictionary<string, long>(StringComparer.Ordinal);
    private readonly IProgress<ModelStagingProgress> _progress;
    private readonly long _expectedTotalBytes;
    private string _lastStatus = string.Empty;
    private double _percent;

    /// <summary>
    /// Creates a tracker for one staging run.
    /// </summary>
    /// <param name="expectedTotalBytes">
    /// What the descriptor says the whole model comes to, or zero when nothing is expected.
    /// </param>
    /// <param name="progress">Where to send each report, or <see langword="null"/> to send none.</param>
    public StagingProgressTracker(long expectedTotalBytes, IProgress<ModelStagingProgress> progress)
    {
        _expectedTotalBytes = expectedTotalBytes;
        _progress = progress;
    }

    /// <summary>
    /// Takes in one report from the store and sends out the report a bar is driven from.
    /// </summary>
    /// <param name="pullProgress">What the store said.</param>
    public void Report(PullProgress pullProgress)
    {
        if (pullProgress == null)
        {
            return;
        }

        _lastStatus = pullProgress.Status ?? _lastStatus;

        if (!string.IsNullOrEmpty(pullProgress.Digest))
        {
            if (pullProgress.TotalBytes > 0L)
            {
                _layerTotals[pullProgress.Digest] = pullProgress.TotalBytes;
            }
            if (pullProgress.CompletedBytes > GetCompleted(pullProgress.Digest))
            {
                _layerCompleted[pullProgress.Digest] = pullProgress.CompletedBytes;
            }
        }

        if (_progress == null)
        {
            return;
        }

        long completed = Sum(_layerCompleted);
        long total = ResolveTotal();
        _progress.Report(new ModelStagingProgress(
            pullProgress.Status,
            pullProgress.Digest,
            pullProgress.TotalBytes,
            pullProgress.CompletedBytes,
            total,
            completed,
            NextPercent(completed, total)));
    }

    /// <summary>
    /// Sends the last report of a successful run, which is always one hundred percent.
    /// </summary>
    /// <param name="totalBytes">The size the finished model turned out to be.</param>
    public void ReportComplete(long totalBytes)
    {
        _percent = 100d;
        if (_progress == null)
        {
            return;
        }

        long total = totalBytes > 0L ? totalBytes : ResolveTotal();
        _progress.Report(new ModelStagingProgress(_lastStatus, null, 0L, 0L, total, total, 100d));
    }

    /// <summary>
    /// The denominator: what the descriptor expects while that is the larger number, and what the layers
    /// add up to once it is not.
    /// </summary>
    /// <returns>The denominator in bytes.</returns>
    private long ResolveTotal()
    {
        long layerSum = Sum(_layerTotals);
        return _expectedTotalBytes > layerSum ? _expectedTotalBytes : layerSum;
    }

    /// <summary>
    /// The percentage to report, never below the one already reported.
    /// </summary>
    /// <param name="completed">The numerator in bytes.</param>
    /// <param name="total">The denominator in bytes.</param>
    /// <returns>The percentage, from zero to one hundred.</returns>
    private double NextPercent(long completed, long total)
    {
        double percent = total > 0L ? completed * 100d / total : 0d;
        if (percent > 100d)
        {
            percent = 100d;
        }
        if (percent > _percent)
        {
            _percent = percent;
        }
        return _percent;
    }

    /// <summary>
    /// How much of one layer has been reported so far.
    /// </summary>
    /// <param name="digest">The layer's digest.</param>
    /// <returns>The byte count, or zero when the layer has not been reported yet.</returns>
    private long GetCompleted(string digest)
    {
        return _layerCompleted.TryGetValue(digest, out long completed) ? completed : 0L;
    }

    /// <summary>
    /// Adds up the values of one of the two tables.
    /// </summary>
    /// <param name="values">The table to add up.</param>
    /// <returns>The sum in bytes.</returns>
    private static long Sum(Dictionary<string, long> values)
    {
        long sum = 0L;
        foreach (KeyValuePair<string, long> entry in values)
        {
            sum += entry.Value;
        }
        return sum;
    }
}
