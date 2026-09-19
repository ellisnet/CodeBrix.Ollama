using System;
using System.Globalization;

namespace ModelQueryTool.Helpers;

/// <summary>
/// Turns a byte count into the short form a person reads in a status line - "21.3 GiB",
/// "858 MiB" - using binary units, which is what the model's publisher and the store both count
/// in.
/// </summary>
public static class ByteSize
{
    private const double Kibibyte = 1024d;
    private const double Mebibyte = Kibibyte * 1024d;
    private const double Gibibyte = Mebibyte * 1024d;

    /// <summary>Writes a byte count in the largest binary unit that leaves a number above one.</summary>
    /// <param name="bytes">The number of bytes; a negative count is treated as none.</param>
    /// <returns>The count with its unit, such as <c>21.3 GiB</c>.</returns>
    public static string Describe(long bytes)
    {
        if (bytes <= 0L)
        {
            return "0 B";
        }

        if (bytes >= Gibibyte)
        {
            return Format(bytes / Gibibyte, "GiB");
        }

        if (bytes >= Mebibyte)
        {
            return Format(bytes / Mebibyte, "MiB");
        }

        if (bytes >= Kibibyte)
        {
            return Format(bytes / Kibibyte, "KiB");
        }

        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    /// <summary>
    /// Writes a byte count in the unit another count sets, so that the two halves of
    /// "so much of so much" never disagree about what they are counting in.
    /// </summary>
    /// <param name="bytes">The number of bytes to write; a negative count is treated as none.</param>
    /// <param name="unitFrom">The count whose unit is used.</param>
    /// <returns>The count with the unit of <paramref name="unitFrom"/>.</returns>
    public static string DescribeInUnitOf(long bytes, long unitFrom)
    {
        if (unitFrom >= Gibibyte)
        {
            return Format(Math.Max(0L, bytes) / Gibibyte, "GiB");
        }

        if (unitFrom >= Mebibyte)
        {
            return Format(Math.Max(0L, bytes) / Mebibyte, "MiB");
        }

        return Describe(bytes);
    }

    private static string Format(double value, string unit) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;
}
