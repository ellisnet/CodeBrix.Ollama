using System.Numerics;
using System.Runtime.Intrinsics.X86;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The options a loaded model runs with, resolved: a thread count that is a number rather than "the default",
/// and one arithmetic path rather than "the widest there is".
/// </summary>
internal sealed class OnnxExecutionSettings
{
    private OnnxExecutionSettings(int threads, OnnxKernelKind kernel)
    {
        Threads = threads;
        Kernel = kernel;
    }

    /// <summary>How many threads the matrix kernels spread their work over. Never below one.</summary>
    internal int Threads { get; }

    /// <summary>The arithmetic path the kernels take.</summary>
    internal OnnxKernelKind Kernel { get; }

    /// <summary>
    /// Resolves the caller's options against the processor.
    /// </summary>
    /// <param name="options">The options, which may leave the thread count unstated.</param>
    /// <returns>The resolved settings.</returns>
    internal static OnnxExecutionSettings Resolve(OnnxRunnerOptions options) =>
        Resolve(options, DefaultThreads());

    /// <summary>
    /// Resolves the caller's options against a stated detected count, which is how the rule is exercised
    /// against a processor other than the one the code is running on.
    /// </summary>
    /// <param name="options">The options, which may leave the thread count unstated.</param>
    /// <param name="detected">What the processor suggests when the caller states no count.</param>
    /// <returns>The resolved settings.</returns>
    /// <remarks>
    /// <see cref="OnnxRunnerOptions.Threads"/> is used exactly as it stands and
    /// <see cref="OnnxRunnerOptions.MaxThreads"/> is ignored for it; the cap bounds only the detected count.
    /// See <see cref="EngineThreadCount"/>, which is the one place the rule is written.
    /// </remarks>
    internal static OnnxExecutionSettings Resolve(OnnxRunnerOptions options, int detected)
    {
        int threads = EngineThreadCount.Resolve(options.Threads, options.MaxThreads, detected);
        return new OnnxExecutionSettings(threads, ResolveKernel(options.KernelPath));
    }

    /// <summary>
    /// The thread count a caller who states none gets, before any cap of theirs: the performance cores where
    /// the processor has two kinds of core and says so, and the physical cores everywhere else.
    /// </summary>
    /// <returns>The count, never below one.</returns>
    /// <remarks>
    /// A matrix multiply is split into equal pieces and finishes with its slowest piece, so a thread on an
    /// efficient core holds up every thread on a fast one. On the laptop this engine was measured on - eight
    /// performance cores and eight efficient ones - sixteen threads is never faster than eight and is up to
    /// eight per cent slower. Where the processor does not say, or has one kind of core, this is one thread
    /// per physical core.
    /// </remarks>
    internal static int DefaultThreads()
    {
        int performance = EnginePerformanceCores.Count();
        return performance > 0 ? performance : EnginePhysicalCores.Count();
    }

    /// <summary>The widest path this processor offers.</summary>
    /// <returns>The path <see cref="OnnxKernelPath.Automatic"/> resolves to here.</returns>
    internal static OnnxKernelKind Widest()
    {
        if (Avx2.IsSupported && Fma.IsSupported) return OnnxKernelKind.Avx2;
        return Vector.IsHardwareAccelerated ? OnnxKernelKind.Vector : OnnxKernelKind.Scalar;
    }

    private static OnnxKernelKind ResolveKernel(OnnxKernelPath path) => path switch
    {
        OnnxKernelPath.Scalar => OnnxKernelKind.Scalar,

        // The portable vector path is asked for by name, so it is taken by name: on a processor with no
        // vector unit at all .NET runs Vector<float> in software, which is slow but still correct, and a
        // caller that asked to measure that path should get it rather than a silent substitution.
        OnnxKernelPath.Vector => OnnxKernelKind.Vector,
        _ => Widest(),
    };
}
