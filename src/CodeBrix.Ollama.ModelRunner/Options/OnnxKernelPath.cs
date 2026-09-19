namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Which arithmetic path the engine's matrix kernels take. The default,
/// <see cref="OnnxKernelPath.Automatic"/>, is the right answer everywhere; the other two exist so that the
/// three paths can be measured and tested against each other on one machine.
/// </summary>
/// <remarks>
/// All three compute the same thing. They do not compute it in the same ORDER, so a sum of many products can
/// differ in its last bits between them, exactly as it does between any two matrix libraries. The test suite
/// holds them to that: every kernel is compared against the scalar path element by element within a stated
/// tolerance, so a machine with no vector unit at all is as correct as this one.
/// </remarks>
public enum OnnxKernelPath
{
    /// <summary>The widest path the processor offers. This is the default and what a caller should use.</summary>
    Automatic = 0,

    /// <summary>The portable vector path, which runs wherever .NET's <c>Vector</c> type is accelerated.</summary>
    Vector = 1,

    /// <summary>One element at a time, with no vector instructions at all. The reference the others are held to.</summary>
    Scalar = 2,
}
