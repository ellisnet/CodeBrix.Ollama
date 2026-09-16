namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// Which attention mask an embedding context uses. Mirrors <c>enum llama_attention_type</c>.
/// </summary>
internal enum LlamaAttentionType : int
{
    /// <summary>Take the model's own setting.</summary>
    Unspecified = -1,

    /// <summary>Each token attends only to the tokens before it.</summary>
    Causal = 0,

    /// <summary>Every token attends to every token.</summary>
    NonCausal = 1,
}
