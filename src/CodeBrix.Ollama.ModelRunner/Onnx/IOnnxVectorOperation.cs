using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The vector form of an element-by-element operator on floats, for the case where both tensors have the same
/// shape and the whole thing is one straight run through memory.
/// </summary>
/// <remarks>
/// A vector lane does exactly what the scalar does, so this path and the scalar path agree to the last bit -
/// there is no reassociation here, unlike in a sum. An operator with no vector form (a power, say) answers
/// <see cref="CanVectorize"/> with <see langword="false"/> and is walked one element at a time.
/// </remarks>
internal interface IOnnxVectorOperation
{
    /// <summary>Whether the operator has a vector form at all.</summary>
    static abstract bool CanVectorize { get; }

    /// <summary>Applies the operator to a whole vector of pairs.</summary>
    /// <param name="left">The left elements.</param>
    /// <param name="right">The right elements.</param>
    /// <returns>The results.</returns>
    static abstract Vector<float> Apply(Vector<float> left, Vector<float> right);
}
