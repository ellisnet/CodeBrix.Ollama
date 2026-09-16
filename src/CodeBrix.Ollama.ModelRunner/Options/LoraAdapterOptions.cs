namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A LoRA adapter to apply to the loaded model.
/// </summary>
public sealed class LoraAdapterOptions
{
    /// <summary>The path of the adapter's GGUF file.</summary>
    public string Path { get; set; }

    /// <summary>The scale the adapter is applied with. Default 1.0.</summary>
    public float Scale { get; set; } = 1.0f;
}
