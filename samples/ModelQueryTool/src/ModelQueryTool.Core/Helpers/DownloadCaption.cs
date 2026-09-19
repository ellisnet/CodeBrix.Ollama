using System;
using System.Globalization;

namespace ModelQueryTool.Helpers;

/// <summary>
/// The one line beside the download bar: how much of the model has arrived, how fast it is
/// arriving, and how long the rest will take.
/// </summary>
/// <remarks>
/// A download of twenty gibibytes runs for a quarter of an hour, so a bar on its own says almost
/// nothing. The rate and the time left are left out entirely until there is a rate to report,
/// rather than shown as zero - a caption that says "0.0 MiB/s" reads like a stall.
/// </remarks>
public static class DownloadCaption
{
    private const double Mebibyte = 1024d * 1024d;
    private const int SecondsPerMinute = 60;
    private const int SecondsPerHour = 60 * SecondsPerMinute;

    /// <summary>Builds the caption.</summary>
    /// <param name="completedBytes">How many bytes have arrived.</param>
    /// <param name="totalBytes">How many bytes the whole download comes to, or zero when that is not known.</param>
    /// <param name="bytesPerSecond">
    /// The smoothed rate in bytes per second, or zero or less when no rate has been measured yet.
    /// </param>
    /// <returns>The caption, such as <c>3.5 GiB / 21.3 GiB - 45.2 MiB/s - 6m 32s left</c>.</returns>
    public static string Format(long completedBytes, long totalBytes, double bytesPerSecond)
    {
        string caption = totalBytes > 0L
            ? ByteSize.DescribeInUnitOf(completedBytes, totalBytes) + " / " + ByteSize.Describe(totalBytes)
            : ByteSize.Describe(completedBytes);

        if (bytesPerSecond <= 0d || double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond))
        {
            return caption;
        }

        caption += " - " + (bytesPerSecond / Mebibyte).ToString("0.0", CultureInfo.InvariantCulture) + " MiB/s";

        long remaining = totalBytes - completedBytes;
        if (totalBytes <= 0L || remaining <= 0L)
        {
            return caption;
        }

        return caption + " - " + DescribeTimeLeft(remaining / bytesPerSecond) + " left";
    }

    /// <summary>
    /// Writes a number of seconds the way a person reads a wait: seconds on their own under a
    /// minute, minutes and seconds under an hour, hours and minutes above it.
    /// </summary>
    /// <param name="seconds">How many seconds are left.</param>
    /// <returns>The wait, such as <c>6m 32s</c>.</returns>
    private static string DescribeTimeLeft(double seconds)
    {
        long whole = (long)Math.Ceiling(seconds);

        if (whole < SecondsPerMinute)
        {
            return whole.ToString(CultureInfo.InvariantCulture) + "s";
        }

        if (whole < SecondsPerHour)
        {
            long minutes = whole / SecondsPerMinute;

            return minutes.ToString(CultureInfo.InvariantCulture) + "m "
                + (whole - (minutes * SecondsPerMinute)).ToString("00", CultureInfo.InvariantCulture) + "s";
        }

        long hours = whole / SecondsPerHour;

        return hours.ToString(CultureInfo.InvariantCulture) + "h "
            + ((whole - (hours * SecondsPerHour)) / SecondsPerMinute).ToString("00", CultureInfo.InvariantCulture) + "m";
    }
}
