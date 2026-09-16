namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// What a context is for. Mirrors <c>enum llama_context_type</c>.
/// </summary>
internal enum LlamaContextType : int
{
    /// <summary>An ordinary inference context.</summary>
    Default = 0,

    /// <summary>A multi-token-prediction context.</summary>
    Mtp = 1,
}
