using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What is known about the native engine library after it has loaded.
/// </summary>
public sealed class NativeRuntimeInfo
{
    /// <summary>The full path the library was loaded from.</summary>
    public string LoadedPath { get; init; }

    /// <summary>The runtime identifier the library was built for, as the library reports it, for example "osx-x64".</summary>
    public string RuntimeIdentifier { get; init; }

    /// <summary>The library's build record: upstream commit and tag, build date and host.</summary>
    public string BuildInfo { get; init; }

    /// <summary>The engine's system-information line: which CPU features and backends are in use.</summary>
    public string SystemInfo { get; init; }

    /// <summary>The compute devices the engine can see.</summary>
    public IReadOnlyList<NativeDeviceInfo> Devices { get; init; }

    /// <summary>Whether the engine reports that memory-mapped loading is supported on this platform.</summary>
    public bool SupportsMemoryMapping { get; init; }

    /// <summary>Whether the engine reports that memory locking is supported on this platform.</summary>
    public bool SupportsMemoryLocking { get; init; }

    /// <summary>Whether the engine reports that GPU offload is supported by this build.</summary>
    public bool SupportsGpuOffload { get; init; }
}
