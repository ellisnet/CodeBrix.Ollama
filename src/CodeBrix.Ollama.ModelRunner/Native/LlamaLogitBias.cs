using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// One entry of a logit-bias sampler: <c>struct llama_logit_bias</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaLogitBias
{
    /// <summary>The token the bias applies to.</summary>
    public int Token;

    /// <summary>The amount added to that token's logit.</summary>
    public float Bias;
}
