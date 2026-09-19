using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The vector form of a one-argument operator on floats. An operator with no vector form - a transcendental
/// function, where .NET offers no vector primitive - answers <see cref="CanVectorize"/> with
/// <see langword="false"/> and is walked one element at a time.
/// </summary>
internal interface IOnnxUnaryVectorOperation
{
    /// <summary>Whether the operator has a vector form at all.</summary>
    static abstract bool CanVectorize { get; }

    /// <summary>Applies the operator to a whole vector of elements.</summary>
    /// <param name="value">The elements.</param>
    /// <returns>The results.</returns>
    static abstract Vector<float> Apply(Vector<float> value);
}
