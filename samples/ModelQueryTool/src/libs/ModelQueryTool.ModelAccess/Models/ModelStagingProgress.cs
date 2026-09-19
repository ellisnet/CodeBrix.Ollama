using System.Globalization;

namespace ModelQueryTool.ModelAccess.Models;

/// <summary>
/// One report from a staging run: what the download is doing, how far the layer in flight has come, and
/// the ONE fraction across the whole model that a progress bar is driven from.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OverallPercent"/> never goes backwards within a run. Its denominator is the descriptor's
/// expected total while that is the larger number and the sum of the layer totals seen so far once it is
/// not, so a model that turns out to be bigger than expected still moves forwards.
/// </para>
/// </remarks>
public sealed class ModelStagingProgress
{
    /// <summary>
    /// Records one report.
    /// </summary>
    /// <param name="status">What the download is doing, in the words the store uses.</param>
    /// <param name="digest">
    /// The layer being fetched, or <see langword="null"/> for a report that is only a status.
    /// </param>
    /// <param name="layerTotalBytes">How large that layer is, or zero when the report is only a status.</param>
    /// <param name="layerCompletedBytes">How much of that layer has arrived.</param>
    /// <param name="overallTotalBytes">The denominator of the one fraction.</param>
    /// <param name="overallCompletedBytes">The numerator of the one fraction.</param>
    /// <param name="overallPercent">The one fraction as a percentage, from zero to one hundred.</param>
    public ModelStagingProgress(
        string status,
        string digest,
        long layerTotalBytes,
        long layerCompletedBytes,
        long overallTotalBytes,
        long overallCompletedBytes,
        double overallPercent)
    {
        Status = status ?? string.Empty;
        Digest = digest;
        LayerTotalBytes = layerTotalBytes;
        LayerCompletedBytes = layerCompletedBytes;
        OverallTotalBytes = overallTotalBytes;
        OverallCompletedBytes = overallCompletedBytes;
        OverallPercent = overallPercent;
    }

    /// <summary>Gets what the download is doing, in the words the store uses.</summary>
    public string Status { get; }

    /// <summary>
    /// Gets the layer being fetched, as <c>sha256:&lt;hex&gt;</c>, or <see langword="null"/> for a
    /// report that is only a status.
    /// </summary>
    public string Digest { get; }

    /// <summary>Gets how large the layer in flight is, or zero for a report that is only a status.</summary>
    public long LayerTotalBytes { get; }

    /// <summary>Gets how much of the layer in flight has arrived.</summary>
    public long LayerCompletedBytes { get; }

    /// <summary>Gets the denominator of the one fraction across the whole model.</summary>
    public long OverallTotalBytes { get; }

    /// <summary>Gets the numerator of the one fraction across the whole model.</summary>
    public long OverallCompletedBytes { get; }

    /// <summary>
    /// Gets the one fraction across the whole model as a percentage, from zero to one hundred. It never
    /// decreases within a staging run, and the last report of a successful run is one hundred.
    /// </summary>
    public double OverallPercent { get; }

    /// <summary>The status and the overall percentage, for a log line or a test failure.</summary>
    /// <returns>The status followed by the percentage.</returns>
    public override string ToString()
    {
        return Status + " " + OverallPercent.ToString("F1", CultureInfo.InvariantCulture) + "%";
    }
}
