using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The input to <c>llama_encode</c> and <c>llama_decode</c>: <c>struct llama_batch</c>, field for field.
/// </summary>
/// <remarks>
/// Every array holds <see cref="NTokens"/> entries. A batch obtained from <c>llama_batch_init</c> owns its
/// arrays and must be released with <c>llama_batch_free</c>; <see cref="LlamaBatchBuffer"/> does that.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaBatch
{
    /// <summary>How many tokens the batch carries.</summary>
    public int NTokens;

    /// <summary>The token ids, used when <see cref="Embd"/> is null.</summary>
    public int* Token;

    /// <summary>Token embeddings, used when <see cref="Token"/> is null.</summary>
    public float* Embd;

    /// <summary>The position of each token in its sequence; null lets the engine track positions.</summary>
    public int* Pos;

    /// <summary>How many sequences each token belongs to.</summary>
    public int* NSeqId;

    /// <summary>The sequence ids each token belongs to; null means sequence 0.</summary>
    public int** SeqId;

    /// <summary>Non-zero where the output for that token is wanted.</summary>
    public sbyte* Logits;
}
