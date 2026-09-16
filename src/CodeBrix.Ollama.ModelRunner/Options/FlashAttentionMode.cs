namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Whether the engine uses its fused flash-attention kernels.
/// </summary>
public enum FlashAttentionMode
{
    /// <summary>Let the engine decide from the model and the backend. The default.</summary>
    Auto = -1,

    /// <summary>Never.</summary>
    Disabled = 0,

    /// <summary>Always; the load fails if the backend cannot.</summary>
    Enabled = 1,
}
