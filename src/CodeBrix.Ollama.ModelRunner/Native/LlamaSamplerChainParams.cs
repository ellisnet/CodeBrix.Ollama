using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// How a sampler chain is created: <c>struct llama_sampler_chain_params</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaSamplerChainParams
{
    /// <summary>Non-zero to skip the performance timers.</summary>
    public byte NoPerf;
}
