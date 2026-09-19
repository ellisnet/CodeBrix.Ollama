using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The largest amount of resident memory this process has held, which is what a test that loads a model of
/// several hundred megabytes wants to record.
/// </summary>
/// <remarks>
/// It is the WHOLE process's high-water mark, test host and all, not the model's own footprint. That is the
/// number that matters for "will this fit on a machine with sixteen gigabytes", and it is the honest one to
/// record: the alternative, subtracting a baseline, would hide what the loader actually asked the operating
/// system for.
/// </remarks>
public static class ProcessMemory
{
    /// <summary>The high-water mark of this process's resident memory, in bytes, or 0 when it is not known.</summary>
    /// <returns>The number of bytes.</returns>
    public static long PeakResident()
    {
        if (OperatingSystem.IsLinux())
        {
            long fromStatus = ReadLinuxHighWaterMark();
            if (fromStatus > 0) return fromStatus;
        }

        try
        {
            return Process.GetCurrentProcess().PeakWorkingSet64;
        }
        catch (PlatformNotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>This process's resident memory right now, in bytes, or 0 when it is not known.</summary>
    /// <returns>The number of bytes.</returns>
    public static long Resident()
    {
        if (OperatingSystem.IsLinux())
        {
            long fromStatus = ReadLinuxField("VmRSS:");
            if (fromStatus > 0) return fromStatus;
        }

        try
        {
            return Process.GetCurrentProcess().WorkingSet64;
        }
        catch (PlatformNotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Forgets the high-water mark, so that what is measured next is one load's own peak rather than
    /// everything this process has ever done.
    /// </summary>
    /// <remarks>
    /// Only Linux offers this, through <c>/proc/self/clear_refs</c>; everywhere else the number stays
    /// process-wide and the measurement says so. A failure here never fails a test.
    /// </remarks>
    /// <returns><see langword="true"/> when the mark was forgotten.</returns>
    public static bool ResetPeak()
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            File.WriteAllText("/proc/self/clear_refs", "5\n");
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Spells a number of bytes in mebibytes.</summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>The text.</returns>
    public static string Mebibytes(long bytes) => bytes == 0
        ? "unknown"
        : (bytes / (1024.0 * 1024.0)).ToString("N0", CultureInfo.InvariantCulture) + " MiB";

    /// <summary>The same number in mebibytes, for a message.</summary>
    /// <returns>The text, for example <c>1,842 MiB</c>.</returns>
    public static string PeakResidentText() => Mebibytes(PeakResident());

    private static long ReadLinuxHighWaterMark() => ReadLinuxField("VmHWM:");

    private static long ReadLinuxField(string field)
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (!line.StartsWith(field, StringComparison.Ordinal)) continue;

                string[] parts = line.Split(
                    (char[])null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2
                    && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out long kilobytes))
                {
                    return kilobytes * 1024;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
    }
}
