namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The storage type of the key or value cache. Quantized caches trade a little accuracy for a much smaller
/// context footprint.
/// </summary>
public enum KvCacheType
{
    /// <summary>The engine's default, 16-bit floating point.</summary>
    Default = 0,

    /// <summary>32-bit floating point.</summary>
    F32 = 1,

    /// <summary>16-bit floating point.</summary>
    F16 = 2,

    /// <summary>16-bit brain floating point.</summary>
    BF16 = 3,

    /// <summary>8-bit quantized (Q8_0).</summary>
    Q8_0 = 4,

    /// <summary>4-bit quantized (Q4_0).</summary>
    Q4_0 = 5,
}
