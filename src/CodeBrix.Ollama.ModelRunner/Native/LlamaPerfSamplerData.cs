using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// A sampler chain's performance counters: <c>struct llama_perf_sampler_data</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaPerfSamplerData
{
    /// <summary>The time spent sampling, in milliseconds.</summary>
    public double TSampleMs;

    /// <summary>How many tokens were sampled.</summary>
    public int NSample;
}
