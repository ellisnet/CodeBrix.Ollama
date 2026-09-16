using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// A sampler as the engine sees it: <c>struct llama_sampler</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaSampler
{
    /// <summary>The sampler's virtual table.</summary>
    public LlamaSamplerInterface* Iface;

    /// <summary>The sampler's own state, opaque to the engine.</summary>
    public void* Ctx;
}
