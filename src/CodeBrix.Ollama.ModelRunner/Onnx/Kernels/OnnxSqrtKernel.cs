using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Sqrt</c>: the square root of every element.
/// </summary>
internal sealed class OnnxSqrtKernel : OnnxKernel, IOnnxUnaryOperation<float>, IOnnxUnaryVectorOperation
{
    /// <inheritdoc />
    internal override string OpType => "Sqrt";

    /// <summary>Whether the operator has a vector form.</summary>
    public static bool CanVectorize => true;

    /// <summary>Applies the operator to one element.</summary>
    /// <param name="value">The element.</param>
    /// <returns>The result.</returns>
    public static float Apply(float value) => MathF.Sqrt(value);

    /// <summary>Applies the operator to a whole vector of elements.</summary>
    /// <param name="value">The elements.</param>
    /// <returns>The results.</returns>
    public static Vector<float> Apply(Vector<float> value) => Vector.SquareRoot(value);

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context) =>
        OnnxElementwise.UnaryFloat<OnnxSqrtKernel>(context);
}
