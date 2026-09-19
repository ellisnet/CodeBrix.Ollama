using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Pow</c>: raises the first tensor to the power of the second, element by element.
/// </summary>
/// <remarks>
/// <para>
/// This is the one element-by-element operator whose two inputs are allowed to carry DIFFERENT types - the
/// specification lets an exponent be an integer while the base is a float - so an exponent of another type is
/// converted to the base's type first and then the ordinary broadcasting walk runs.
/// </para>
/// <para>
/// The small whole-number exponents are multiplied out rather than sent through a general power function. A
/// root-mean-square normalization squares its input through this operator on every layer of every step, and
/// a product is both exact and far quicker than the general routine, which is itself what a general routine
/// would do with an exponent of two.
/// </para>
/// </remarks>
internal sealed class OnnxPowKernel : OnnxKernel,
    IOnnxBinaryOperation<float>,
    IOnnxBinaryOperation<long>,
    IOnnxBinaryOperation<int>,
    IOnnxVectorOperation
{
    /// <inheritdoc />
    internal override string OpType => "Pow";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <summary>Whether the operator has a vector form. It has not.</summary>
    public static bool CanVectorize => false;

    /// <summary>Raises one float to the power of another.</summary>
    /// <param name="left">The base.</param>
    /// <param name="right">The exponent.</param>
    /// <returns>The result.</returns>
    public static float Apply(float left, float right)
    {
        if (right == 2f) return left * left;
        if (right == 1f) return left;
        if (right == 0f) return 1f;
        if (right == 3f) return left * left * left;
        return MathF.Pow(left, right);
    }

    /// <summary>Raises one 64-bit integer to the power of another.</summary>
    /// <param name="left">The base.</param>
    /// <param name="right">The exponent.</param>
    /// <returns>The result, and nought for a negative exponent of anything but one.</returns>
    public static long Apply(long left, long right)
    {
        if (right < 0) return left == 1L ? 1L : (left == -1L ? (right % 2 == 0 ? 1L : -1L) : 0L);

        long result = 1L;
        long value = left;
        long power = right;
        while (power > 0)
        {
            if ((power & 1L) != 0L) result = unchecked(result * value);
            power >>= 1;
            if (power > 0) value = unchecked(value * value);
        }

        return result;
    }

    /// <summary>Raises one 32-bit integer to the power of another.</summary>
    /// <param name="left">The base.</param>
    /// <param name="right">The exponent.</param>
    /// <returns>The result, and nought for a negative exponent of anything but one.</returns>
    public static int Apply(int left, int right) => unchecked((int)Apply((long)left, (long)right));

    /// <summary>Never called: the operator has no vector form.</summary>
    /// <param name="left">The bases.</param>
    /// <param name="right">The exponents.</param>
    /// <returns>Nothing; it throws.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static Vector<float> Apply(Vector<float> left, Vector<float> right) =>
        throw new InvalidOperationException("Pow has no vector form and is never asked for one.");

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue left = context.RequireInput(0);
        OnnxValue right = context.RequireInput(1);
        if (left.ElementType == right.ElementType)
        {
            OnnxElementwise.Binary<OnnxPowKernel>(context, left, right);
            return;
        }

        OnnxValue exponent = OnnxValue.Allocate(
            context.Arena, left.ElementType, (long[])right.Shape.Clone(), true);
        try
        {
            OnnxConvert.Convert(right, exponent);
            OnnxElementwise.Binary<OnnxPowKernel>(context, left, exponent);
        }
        finally
        {
            exponent.Release(context.Arena);
        }
    }
}
