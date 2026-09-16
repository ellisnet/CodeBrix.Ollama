namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml-cpu.h;


/// <summary>
/// How the CPU backend spreads work over NUMA nodes. Mirrors <c>enum ggml_numa_strategy</c>.
/// </summary>
internal enum GgmlNumaStrategy : int
{
    /// <summary>No NUMA handling.</summary>
    Disabled = 0,

    /// <summary>Spread threads over every node.</summary>
    Distribute = 1,

    /// <summary>Keep every thread on one node.</summary>
    Isolate = 2,

    /// <summary>Follow the process's numactl mask.</summary>
    Numactl = 3,

    /// <summary>Mirror the weights on every node.</summary>
    Mirror = 4,
}
