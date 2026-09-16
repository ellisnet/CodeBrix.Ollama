namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One compute device the engine can see.
/// </summary>
public sealed class NativeDeviceInfo
{
    /// <summary>The device's short name, for example "CPU" or "Metal".</summary>
    public string Name { get; init; }

    /// <summary>The device's description, for example the processor or GPU model.</summary>
    public string Description { get; init; }

    /// <summary>The device's kind: "CPU", "GPU", "IGPU", "ACCEL" or "META"; "UNKNOWN" for a kind this library does not recognize.</summary>
    public string Type { get; init; }

    /// <summary>The device's total memory in bytes, when known.</summary>
    public ulong TotalMemory { get; init; }

    /// <summary>The device's free memory in bytes at the time of the query, when known.</summary>
    public ulong FreeMemory { get; init; }
}
