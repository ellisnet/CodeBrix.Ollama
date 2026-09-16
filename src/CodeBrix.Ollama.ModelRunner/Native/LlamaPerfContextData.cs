using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// A context's performance counters: <c>struct llama_perf_context_data</c>. Every time is in milliseconds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LlamaPerfContextData
{
    /// <summary>The absolute start time.</summary>
    public double TStartMs;

    /// <summary>The time the model took to load.</summary>
    public double TLoadMs;

    /// <summary>The time spent processing prompts.</summary>
    public double TPEvalMs;

    /// <summary>The time spent generating tokens.</summary>
    public double TEvalMs;

    /// <summary>How many prompt tokens were processed.</summary>
    public int NPEval;

    /// <summary>How many tokens were generated.</summary>
    public int NEval;

    /// <summary>How many times a compute graph was reused.</summary>
    public int NReused;
}
