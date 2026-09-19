namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// How an ONNX graph is loaded and run. Every property has a default that runs a decoder well; a caller that
/// sets nothing gets one thread per performance core, the widest kernels the processor offers and buffer
/// reuse switched on.
/// </summary>
public sealed class OnnxRunnerOptions
{
    /// <summary>
    /// The number of threads the matrix kernels spread their work over. <see langword="null"/> (the default)
    /// uses the number of PERFORMANCE cores on a processor that has two kinds of core and says which is
    /// which, the number of physical cores on a processor whose cores are all alike, and the logical count
    /// when neither can be found out - bounded by <see cref="MaxThreads"/> where one is set. A value here is
    /// used EXACTLY as it stands: it is never clamped, and <see cref="MaxThreads"/> is ignored for it. Below 1
    /// is <see cref="System.ArgumentOutOfRangeException"/>.
    /// </summary>
    /// <remarks>
    /// A matrix multiply is split into equal pieces and is not finished until its slowest piece is, so a
    /// thread running on an efficient core holds up every thread running on a fast one: on a processor that
    /// mixes the two, fewer threads than cores is measurably faster. That is the default's whole reason, and
    /// it is a default rather than a rule - a machine that matters is worth measuring.
    /// </remarks>
    public int? Threads { get; set; }

    /// <summary>
    /// The largest number of threads this engine may choose BY ITSELF, or <see langword="null"/> (the default)
    /// for no cap. It is for an application that ships to machines it has never seen: "use what this machine
    /// has, but never more than this" - four on a four-core machine, and this number on a large server. It
    /// bounds ONLY the automatic choice, so it does nothing once <see cref="Threads"/> is set. Below 1 is
    /// <see cref="System.ArgumentOutOfRangeException"/>.
    /// </summary>
    public int? MaxThreads { get; set; }

    /// <summary>
    /// Which arithmetic path the matrix kernels take. Default <see cref="OnnxKernelPath.Automatic"/>, which
    /// is the widest the processor offers.
    /// </summary>
    public OnnxKernelPath KernelPath { get; set; } = OnnxKernelPath.Automatic;

    /// <summary>
    /// Whether a run reuses the buffers of tensors it has finished with rather than allocating a new one for
    /// every node. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Reuse is what keeps a decode step's allocation flat instead of proportional to the size of the graph,
    /// and it is safe: a buffer is handed out again only once every node that could read it has run, and a
    /// tensor a run hands BACK is never reused. Switching it off allocates every intermediate tensor
    /// separately, which is slower and hungrier and is there so that the two can be compared - a difference
    /// between them would be a reuse defect.
    /// </remarks>
    public bool ReuseBuffers { get; set; } = true;

    /// <summary>Makes an independent copy, so that a caller's later edits cannot change a loaded model.</summary>
    /// <returns>The copy.</returns>
    internal OnnxRunnerOptions Copy() => new OnnxRunnerOptions
    {
        Threads = Threads,
        MaxThreads = MaxThreads,
        KernelPath = KernelPath,
        ReuseBuffers = ReuseBuffers,
    };
}
