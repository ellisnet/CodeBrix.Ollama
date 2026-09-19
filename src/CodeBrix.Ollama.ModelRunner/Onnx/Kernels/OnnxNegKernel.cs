using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Neg</c>: the negative of every element.
/// </summary>
/// <remarks>
/// It is the one one-argument operator in these graphs that is applied to whole integers as well as to
/// floats - a rotary embedding negates half of a float tensor, and shape arithmetic occasionally negates an
/// index - so it answers for all three element types.
/// </remarks>
internal sealed class OnnxNegKernel : OnnxKernel,
    IOnnxUnaryOperation<float>,
    IOnnxUnaryOperation<long>,
    IOnnxUnaryOperation<int>,
    IOnnxUnaryVectorOperation
{
    /// <inheritdoc />
    internal override string OpType => "Neg";

    /// <summary>Whether the operator has a vector form. It has.</summary>
    public static bool CanVectorize => true;

    /// <summary>Negates one float.</summary>
    /// <param name="value">The element.</param>
    /// <returns>Its negative.</returns>
    public static float Apply(float value) => -value;

    /// <summary>Negates one 64-bit integer.</summary>
    /// <param name="value">The element.</param>
    /// <returns>Its negative.</returns>
    public static long Apply(long value) => -value;

    /// <summary>Negates one 32-bit integer.</summary>
    /// <param name="value">The element.</param>
    /// <returns>Its negative.</returns>
    public static int Apply(int value) => -value;

    /// <summary>Negates a whole vector of floats.</summary>
    /// <param name="value">The elements.</param>
    /// <returns>Their negatives.</returns>
    public static Vector<float> Apply(Vector<float> value) => -value;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        switch (input.ElementType)
        {
            case OnnxElementType.Float:
                OnnxElementwise.UnaryFloat<OnnxNegKernel>(context);
                return;
            case OnnxElementType.Int64:
                OnnxElementwise.UnaryTyped<OnnxNegKernel, long>(context, input);
                return;
            case OnnxElementType.Int32:
                OnnxElementwise.UnaryTyped<OnnxNegKernel, int>(context, input);
                return;
            default:
                throw context.Fail("it cannot negate a " + OnnxTensor.Name(input.ElementType) + " tensor.");
        }
    }
}
