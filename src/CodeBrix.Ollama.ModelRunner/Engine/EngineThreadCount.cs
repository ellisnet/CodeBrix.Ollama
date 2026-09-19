using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The one rule by which either engine turns a caller's thread options into the number of threads it really
/// runs with.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE, AND IT IS THE SAME ON BOTH ROADS. A count the caller asked for is used EXACTLY as it stands -
/// that is the caller's decision, and a cap has nothing to say about it, even when the count oversubscribes
/// the machine. Only the AUTOMATIC count - the one the library works out from the processor because the
/// caller stated none - is bounded by the cap, and the answer is never below one.
/// </para>
/// <para>
/// WHY A CAP AND NOT SIMPLY A COUNT. An application that ships to machines it has never seen cannot name a
/// number: four threads wastes a large server and sixteen oversubscribes a small laptop. A cap says "use what
/// this machine has, but never more than this", which is one sentence for both.
/// </para>
/// <para>
/// The detected count is passed IN rather than discovered here, so that the rule can be exercised against any
/// processor rather than only against the one the tests happen to run on.
/// </para>
/// </remarks>
internal static class EngineThreadCount
{
    /// <summary>Resolves the thread count a run should use.</summary>
    /// <param name="requested">The count the caller asked for, or <see langword="null"/> for none.</param>
    /// <param name="maximum">
    /// The largest AUTOMATIC count the caller will accept, or <see langword="null"/> for no cap. It is ignored
    /// when <paramref name="requested"/> has a value.
    /// </param>
    /// <param name="detected">What the processor suggests when the caller asked for nothing.</param>
    /// <returns>The count, never below one.</returns>
    internal static int Resolve(int? requested, int? maximum, int detected)
    {
        if (requested.HasValue) return Math.Max(1, requested.Value);

        int count = maximum.HasValue && maximum.Value < detected ? maximum.Value : detected;
        return Math.Max(1, count);
    }
}
