namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The model architecture a conversion reads a checkpoint as.
/// </summary>
public enum CheckpointArchitecture
{
    /// <summary>Decide from the checkpoint's <c>config.json</c>.</summary>
    Auto = 0,

    /// <summary>
    /// The Llama family: the architecture the great majority of published decoder-only checkpoints declare,
    /// whatever the model is called.
    /// </summary>
    Llama = 1
}
