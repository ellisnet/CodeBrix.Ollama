using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Reports on the loaded native engine: where it came from, what build it is and what it can see.
/// </summary>
internal static class NativeRuntime
{
    /// <summary>
    /// Loads the engine if it is not loaded already and describes it.
    /// </summary>
    /// <returns>The description.</returns>
    /// <exception cref="NativeLibraryException">The engine could not be loaded or is the wrong build.</exception>
    public static NativeRuntimeInfo Describe()
    {
        NativeLibraryLoader.EnsureLoaded();

        return new NativeRuntimeInfo
        {
            LoadedPath = NativeLibraryLoader.LoadedPath,
            RuntimeIdentifier = NativeLibraryLoader.ReportedRuntimeIdentifier,
            BuildInfo = NativeLibraryLoader.BuildInfo,
            SystemInfo = SystemInfo(),
            Devices = Devices(),
            SupportsMemoryMapping = NativeMethods.llama_supports_mmap(),
            SupportsMemoryLocking = NativeMethods.llama_supports_mlock(),
            SupportsGpuOffload = NativeMethods.llama_supports_gpu_offload(),
        };
    }

    /// <summary>The engine's system-information line: the CPU features and backends it is using.</summary>
    /// <returns>The line, with any trailing whitespace removed.</returns>
    public static unsafe string SystemInfo()
    {
        NativeLibraryLoader.EnsureLoaded();
        string text = NativeLibraryLoader.ReadUtf8(NativeMethods.llama_print_system_info());
        return text == null ? string.Empty : text.Trim();
    }

    /// <summary>The compute devices the engine can see, in the order it registered them.</summary>
    /// <returns>The devices; there is always at least the CPU.</returns>
    public static unsafe IReadOnlyList<NativeDeviceInfo> Devices()
    {
        NativeLibraryLoader.EnsureLoaded();

        nuint count = NativeMethods.ggml_backend_dev_count();
        List<NativeDeviceInfo> devices = new List<NativeDeviceInfo>((int)count);

        for (nuint i = 0; i < count; i++)
        {
            IntPtr device = NativeMethods.ggml_backend_dev_get(i);
            if (device == IntPtr.Zero) continue;

            nuint free = 0;
            nuint total = 0;
            NativeMethods.ggml_backend_dev_memory(device, &free, &total);

            devices.Add(new NativeDeviceInfo
            {
                Name = NativeLibraryLoader.ReadUtf8(NativeMethods.ggml_backend_dev_name(device)) ?? string.Empty,
                Description = NativeLibraryLoader.ReadUtf8(NativeMethods.ggml_backend_dev_description(device)) ?? string.Empty,
                Type = TypeName(NativeMethods.ggml_backend_dev_type(device)),
                FreeMemory = (ulong)free,
                TotalMemory = (ulong)total,
            });
        }

        return devices;
    }

    /// <summary>The name the public contract uses for a device kind.</summary>
    /// <param name="type">The kind.</param>
    /// <returns>"CPU", "GPU", "IGPU", "ACCEL", "META" or "UNKNOWN".</returns>
    internal static string TypeName(GgmlBackendDevType type) =>
        type switch
        {
            GgmlBackendDevType.Cpu => "CPU",
            GgmlBackendDevType.Gpu => "GPU",
            GgmlBackendDevType.IGpu => "IGPU",
            GgmlBackendDevType.Accel => "ACCEL",
            GgmlBackendDevType.Meta => "META",
            _ => "UNKNOWN",
        };
}
