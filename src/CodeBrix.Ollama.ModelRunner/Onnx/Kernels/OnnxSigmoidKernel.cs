using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Sigmoid</c>: the logistic function of every element, 1 / (1 + e to the minus x).
/// </summary>
/// <remarks>
/// A large negative input makes the exponential overflow to infinity and the quotient nought, which is the
/// right answer and needs no guard of its own.
/// </remarks>
internal sealed class OnnxSigmoidKernel : OnnxKernel, IOnnxUnaryOperation<float>, IOnnxUnaryVectorOperation
{
    /// <inheritdoc />
    internal override string OpType => "Sigmoid";

    /// <summary>Whether the operator has a vector form.</summary>
    public static bool CanVectorize => false;

    /// <summary>Applies the operator to one element.</summary>
    /// <param name="value">The element.</param>
    /// <returns>The result.</returns>
    public static float Apply(float value) => 1f / (1f + MathF.Exp(-value));

    /// <summary>Applies the operator to a whole vector of elements.</summary>
    /// <param name="value">The elements.</param>
    /// <returns>The results.</returns>
    public static Vector<float> Apply(Vector<float> value) => throw new InvalidOperationException("Sigmoid has no vector form and is never asked for one.");

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context) =>
        OnnxElementwise.UnaryFloat<OnnxSigmoidKernel>(context);
}
