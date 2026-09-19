using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Sin</c>: the sine of every element, in radians.
/// </summary>
internal sealed class OnnxSinKernel : OnnxKernel, IOnnxUnaryOperation<float>, IOnnxUnaryVectorOperation
{
    /// <inheritdoc />
    internal override string OpType => "Sin";

    /// <summary>Whether the operator has a vector form.</summary>
    public static bool CanVectorize => false;

    /// <summary>Applies the operator to one element.</summary>
    /// <param name="value">The element.</param>
    /// <returns>The result.</returns>
    public static float Apply(float value) => MathF.Sin(value);

    /// <summary>Applies the operator to a whole vector of elements.</summary>
    /// <param name="value">The elements.</param>
    /// <returns>The results.</returns>
    public static Vector<float> Apply(Vector<float> value) => throw new InvalidOperationException("Sin has no vector form and is never asked for one.");

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context) =>
        OnnxElementwise.UnaryFloat<OnnxSinKernel>(context);
}
