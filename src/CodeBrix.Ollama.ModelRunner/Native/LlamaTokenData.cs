using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// One candidate token as a sampler sees it: <c>struct llama_token_data</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaTokenData
{
    /// <summary>The token id.</summary>
    public int Id;

    /// <summary>The token's log-odds.</summary>
    public float Logit;

    /// <summary>The token's probability, once a sampler has computed one.</summary>
    public float P;
}
