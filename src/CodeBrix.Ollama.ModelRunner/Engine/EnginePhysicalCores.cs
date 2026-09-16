using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// How many physical cores the machine has, which is the thread count a CPU inference run wants.
/// </summary>
/// <remarks>
/// <para>
/// Inference is memory-bandwidth bound and gains nothing from a second hardware thread on the same core; on
/// a hyper-threaded processor, running one thread per LOGICAL core is measurably slower than one per
/// physical core. <see cref="Environment.ProcessorCount"/> reports logical cores, so the physical count is
/// asked for directly where the platform offers it:
/// </para>
/// <list type="bullet">
/// <item><description>macOS: <c>sysctlbyname("hw.physicalcpu")</c>.</description></item>
/// <item><description>Linux: the number of distinct (physical id, core id) pairs in <c>/proc/cpuinfo</c>.</description></item>
/// <item><description>Anything else, or any failure: <see cref="Environment.ProcessorCount"/>, which is what
/// the contract promises as the fallback.</description></item>
/// </list>
/// <para>
/// The answer is worked out once and cached: it cannot change while the process runs.
/// </para>
/// </remarks>
internal static unsafe partial class EnginePhysicalCores
{
    private static int cached;

    /// <summary>The number of physical cores, never below one.</summary>
    /// <returns>The count.</returns>
    public static int Count()
    {
        int known = cached;
        if (known > 0) return known;

        known = Detect();
        if (known < 1) known = 1;
        cached = known;
        return known;
    }

    [LibraryImport("libc", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int SysctlByName(string name, void* oldp, nuint* oldlenp, void* newp, nuint newlen);

    private static int Detect()
    {
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                int value = ReadSysctl("hw.physicalcpu");
                if (value > 0) return value;
            }
            else if (OperatingSystem.IsLinux())
            {
                int value = ReadProcCpuInfo();
                if (value > 0) return value;
            }
        }
        catch (Exception)
        {
            // Any platform that answers differently, or not at all, falls back to the logical count.
        }

        return Environment.ProcessorCount;
    }

    private static int ReadSysctl(string name)
    {
        int value = 0;
        nuint size = sizeof(int);
        int result = SysctlByName(name, &value, &size, null, 0);
        return result == 0 ? value : 0;
    }

    private static int ReadProcCpuInfo()
    {
        const string path = "/proc/cpuinfo";
        if (!File.Exists(path)) return 0;

        HashSet<string> cores = new HashSet<string>(StringComparer.Ordinal);
        string physical = null;
        string core = null;

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                if (physical != null && core != null) cores.Add(physical + ":" + core);
                physical = null;
                core = null;
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon < 0) continue;

            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();

            if (string.Equals(key, "physical id", StringComparison.Ordinal)) physical = value;
            else if (string.Equals(key, "core id", StringComparison.Ordinal)) core = value;
        }

        if (physical != null && core != null) cores.Add(physical + ":" + core);

        return cores.Count;
    }
}
