namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// How embeddings are pooled over a sequence. Mirrors <c>enum llama_pooling_type</c>.
/// </summary>
internal enum LlamaPoolingType : int
{
    /// <summary>Take the model's own setting.</summary>
    Unspecified = -1,

    /// <summary>No pooling: one embedding per token.</summary>
    None = 0,

    /// <summary>The mean of the token embeddings.</summary>
    Mean = 1,

    /// <summary>The first token's embedding.</summary>
    Cls = 2,

    /// <summary>The last token's embedding.</summary>
    Last = 3,

    /// <summary>The reranking classification head.</summary>
    Rank = 4,
}
