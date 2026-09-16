namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a model can do, inferred the way Ollama infers it: from the GGUF metadata (pooling type,
/// vision and audio blocks), the projector layers, and the chat template text.
/// </summary>
public enum ModelCapability
{
    /// <summary>Text completion and chat.</summary>
    Completion,

    /// <summary>Tool (function) calling; the chat template mentions tools.</summary>
    Tools,

    /// <summary>Fill-in-the-middle insertion; the Go template uses a suffix variable.</summary>
    Insert,

    /// <summary>Image input; a vision projector or vision blocks are present.</summary>
    Vision,

    /// <summary>Audio input; an audio encoder is present.</summary>
    Audio,

    /// <summary>Embedding output; the GGUF declares a pooling type.</summary>
    Embedding,

    /// <summary>Thinking (reasoning) output; the chat template mentions it.</summary>
    Thinking
}
