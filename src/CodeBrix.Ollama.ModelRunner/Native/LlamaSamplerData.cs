using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The graph tensors a backend sampler works on: <c>struct llama_sampler_data</c>.
/// </summary>
/// <remarks>Each field is a <c>struct ggml_tensor *</c>; the graph types are not bound here.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaSamplerData
{
    /// <summary>The logits tensor.</summary>
    public void* Logits;

    /// <summary>The probabilities tensor.</summary>
    public void* Probs;

    /// <summary>The sampled-token tensor.</summary>
    public void* Sampled;

    /// <summary>The candidate-ids tensor.</summary>
    public void* Candidates;
}
