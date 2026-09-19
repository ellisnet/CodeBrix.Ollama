using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// How many PERFORMANCE cores the machine has, on the processors that have two kinds. It is engine-neutral:
/// it answers a question about the PROCESSOR, and each engine decides for itself whether that answer is the
/// thread count it wants.
/// </summary>
/// <remarks>
/// <para>
/// A PROCESSOR THAT MIXES FAST AND EFFICIENT CORES CAN BREAK "ONE THREAD PER PHYSICAL CORE". Where a matrix
/// multiply is split into equal pieces the node is not finished until the last piece is, so the slowest
/// thread sets the pace: adding a thread on a core that runs at two-thirds the speed makes every other thread
/// wait for it. The managed ONNX engine measures exactly that - on a laptop with eight performance cores and
/// eight efficient ones it is never faster at sixteen threads than at eight, and up to eight per cent slower -
/// which is why that engine's default is this count. The NATIVE engine was measured on the same machine and
/// behaves differently: it is FASTER on every physical core than on the performance cores alone, so its
/// default is <see cref="EnginePhysicalCores"/> and it does not call this type. See MAINTAINER-README.
/// </para>
/// <para>
/// THIS IS A SEPARATE TYPE FROM <see cref="EnginePhysicalCores"/> ON PURPOSE. That one answers a different
/// question - how many physical cores are there - and the two are wanted independently, so the platform calls
/// below are written out again here rather than shared.
/// </para>
/// <list type="bullet">
/// <item><description>Linux, Intel-style hybrid: <c>/sys/devices/cpu_core/cpus</c> lists the logical
/// processors of the performance cores, which are then counted as physical cores through
/// <c>/proc/cpuinfo</c>.</description></item>
/// <item><description>Linux, ARM big.LITTLE: the largest <c>cpu_capacity</c> any processor reports, and how
/// many processors report it.</description></item>
/// <item><description>macOS: <c>sysctlbyname("hw.perflevel0.physicalcpu")</c> - level 0 is the fastest.</description></item>
/// <item><description>Windows: <c>GetLogicalProcessorInformationEx</c> filtered to <c>RelationProcessorCore</c>,
/// counting the cores whose efficiency class is the highest any core reports.</description></item>
/// <item><description>Anything else, any failure, or a processor whose cores are all alike: nought, and the
/// caller falls back to the physical count.</description></item>
/// </list>
/// <para>
/// The answer is worked out once and cached: it cannot change while the process runs.
/// </para>
/// </remarks>
internal static unsafe partial class EnginePerformanceCores
{
    /// <summary>The <c>LOGICAL_PROCESSOR_RELATIONSHIP</c> value that asks Windows for one record per core.</summary>
    private const int RelationProcessorCore = 0;

    /// <summary>The Win32 error a sizing call answers with when it was handed no buffer.</summary>
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>Where Linux lists the logical processors belonging to the performance cores.</summary>
    private const string HybridCorePath = "/sys/devices/cpu_core/cpus";

    /// <summary>Where Linux keeps one processor's relative capacity.</summary>
    private const string CapacityFormat = "/sys/devices/system/cpu/cpu{0}/cpu_capacity";

    private static int cached = -1;

    /// <summary>
    /// The number of performance cores, or nought when the machine has only one kind of core or does not say.
    /// </summary>
    /// <returns>The count, or nought.</returns>
    public static int Count()
    {
        int known = cached;
        if (known >= 0) return known;

        known = Detect();
        if (known < 0) known = 0;
        cached = known;
        return known;
    }

    [LibraryImport("libc", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int SysctlByName(string name, void* oldp, nuint* oldlenp, void* newp, nuint newlen);

    [LibraryImport("kernel32", EntryPoint = "GetLogicalProcessorInformationEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(
        int relationshipType, byte* buffer, uint* returnedLength);

    private static int Detect()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindowsPerformanceCores();
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                return ReadSysctl("hw.perflevel0.physicalcpu");
            }

            if (OperatingSystem.IsLinux())
            {
                int hybrid = ReadLinuxHybridCores();
                return hybrid > 0 ? hybrid : ReadLinuxCapacityCores();
            }
        }
        catch (Exception)
        {
            // A platform that answers differently, or not at all, leaves the caller with the physical count.
        }

        return 0;
    }

    private static int ReadWindowsPerformanceCores()
    {
        // The first call is a sizing call: no buffer, and the answer is "insufficient buffer" plus the length
        // the real call needs. Any other outcome means the API is not behaving as documented.
        uint length = 0;
        if (GetLogicalProcessorInformationEx(RelationProcessorCore, null, &length)
            || Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer
            || length == 0)
        {
            return 0;
        }

        byte[] buffer = new byte[length];
        fixed (byte* start = buffer)
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, start, &length)) return 0;

            // The buffer is a run of variable-length SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX records, each
            // beginning with its Relationship and its Size. With this filter every record describes one
            // physical core, and the PROCESSOR_RELATIONSHIP that follows those two fields begins with a Flags
            // byte and then the EfficiencyClass - where a HIGHER number is the faster, less efficient core.
            int best = -1;
            int cores = 0;
            uint offset = 0;

            while (offset + 10 <= length)
            {
                int relationship = *(int*)(start + offset);
                uint size = *(uint*)(start + offset + 4);
                if (size < 10) break;

                if (relationship == RelationProcessorCore)
                {
                    int efficiency = *(start + offset + 9);
                    if (efficiency > best)
                    {
                        best = efficiency;
                        cores = 1;
                    }
                    else if (efficiency == best)
                    {
                        cores++;
                    }
                }

                offset += size;
            }

            return cores;
        }
    }

    private static int ReadSysctl(string name)
    {
        int value = 0;
        nuint size = sizeof(int);
        int result = SysctlByName(name, &value, &size, null, 0);
        return result == 0 ? value : 0;
    }

    private static int ReadLinuxHybridCores()
    {
        if (!File.Exists(HybridCorePath)) return 0;

        HashSet<int> processors = ParseList(File.ReadAllText(HybridCorePath));
        if (processors.Count == 0) return 0;

        // Those are LOGICAL processors, and a performance core usually carries two of them. The distinct
        // (physical id, core id) pairs among them are the physical performance cores.
        int physical = CountPhysicalCores(processors);
        return physical > 0 ? physical : processors.Count;
    }

    private static int ReadLinuxCapacityCores()
    {
        // A big.LITTLE machine gives every processor a capacity relative to the largest, which is 1024. The
        // performance cores are the ones that report the highest number; when every processor reports the
        // same, there is only one kind of core and there is nothing to say.
        int count = Environment.ProcessorCount;
        long best = 0;
        long lowest = long.MaxValue;
        HashSet<int> fastest = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            string path = string.Format(CultureInfo.InvariantCulture, CapacityFormat, i);
            if (!File.Exists(path)) return 0;
            if (!long.TryParse(
                File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out long capacity))
            {
                return 0;
            }

            lowest = Math.Min(lowest, capacity);
            if (capacity > best)
            {
                best = capacity;
                fastest.Clear();
            }

            if (capacity == best) fastest.Add(i);
        }

        if (best <= 0 || best == lowest) return 0;

        int physical = CountPhysicalCores(fastest);
        return physical > 0 ? physical : fastest.Count;
    }

    private static int CountPhysicalCores(HashSet<int> processors)
    {
        const string path = "/proc/cpuinfo";
        if (!File.Exists(path)) return 0;

        HashSet<string> cores = new HashSet<string>(StringComparer.Ordinal);
        int processor = -1;
        string physical = null;
        string core = null;

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                Record(cores, processors, processor, physical, core);
                processor = -1;
                physical = null;
                core = null;
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon < 0) continue;

            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();

            if (string.Equals(key, "processor", StringComparison.Ordinal))
            {
                processor = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    ? index
                    : -1;
            }
            else if (string.Equals(key, "physical id", StringComparison.Ordinal))
            {
                physical = value;
            }
            else if (string.Equals(key, "core id", StringComparison.Ordinal))
            {
                core = value;
            }
        }

        Record(cores, processors, processor, physical, core);
        return cores.Count;
    }

    private static void Record(
        HashSet<string> cores, HashSet<int> wanted, int processor, string physical, string core)
    {
        if (processor < 0 || physical == null || core == null) return;
        if (!wanted.Contains(processor)) return;
        cores.Add(physical + ":" + core);
    }

    /// <summary>Reads a Linux processor list - "0-15", "0,2,4", "0-3,8-11" - into the numbers it names.</summary>
    /// <param name="text">The list as the kernel writes it.</param>
    /// <returns>The processor numbers.</returns>
    internal static HashSet<int> ParseList(string text)
    {
        HashSet<int> numbers = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(text)) return numbers;

        foreach (string part in text.Trim().Split(','))
        {
            string piece = part.Trim();
            if (piece.Length == 0) continue;

            int dash = piece.IndexOf('-');
            if (dash < 0)
            {
                if (int.TryParse(piece, NumberStyles.Integer, CultureInfo.InvariantCulture, out int one))
                {
                    numbers.Add(one);
                }

                continue;
            }

            if (!int.TryParse(
                    piece.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int first)
                || !int.TryParse(
                    piece.Substring(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int last))
            {
                continue;
            }

            // A list the kernel writes is always ascending; one that is not is read as the pair it names.
            if (last < first) (first, last) = (last, first);
            for (int i = first; i <= last; i++) numbers.Add(i);
        }

        return numbers;
    }
}
