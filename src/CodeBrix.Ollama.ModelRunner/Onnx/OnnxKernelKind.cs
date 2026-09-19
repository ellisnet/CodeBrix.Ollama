namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The arithmetic path a kernel actually takes, once <see cref="OnnxKernelPath.Automatic"/> has been resolved
/// against the processor the engine is running on.
/// </summary>
internal enum OnnxKernelKind
{
    /// <summary>One element at a time. Correct everywhere and the reference the other two are held to.</summary>
    Scalar = 0,

    /// <summary>.NET's portable <c>Vector</c>, which is as wide as the processor's own vector registers.</summary>
    Vector = 1,

    /// <summary>256-bit AVX2 with fused multiply-add, on the x86-64 processors that have both.</summary>
    Avx2 = 2,
}
