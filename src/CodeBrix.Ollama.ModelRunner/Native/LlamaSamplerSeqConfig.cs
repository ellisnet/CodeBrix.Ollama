using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// Binds a backend sampler chain to one sequence: <c>struct llama_sampler_seq_config</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaSamplerSeqConfig
{
    /// <summary>The sequence the chain samples for.</summary>
    public int SeqId;

    /// <summary>The <c>llama_sampler</c> chain, which the caller keeps alive.</summary>
    public void* Sampler;
}
