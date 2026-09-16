using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The candidate set a sampler works on: <c>struct llama_token_data_array</c>.
/// </summary>
/// <remarks>Samplers may replace <see cref="Data"/> and shrink <see cref="Size"/>.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaTokenDataArray
{
    /// <summary>The candidates.</summary>
    public LlamaTokenData* Data;

    /// <summary>How many candidates there are.</summary>
    public nuint Size;

    /// <summary>The index in <see cref="Data"/> of the selected candidate, not a token id.</summary>
    public long Selected;

    /// <summary>Non-zero when <see cref="Data"/> is sorted by descending logit. Never assume it is.</summary>
    public byte Sorted;
}
