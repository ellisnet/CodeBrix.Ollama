using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/core/providers/cpu/quantization/matmul_integer.cc@v1.30.0

/// <summary>
/// <c>MatMulInteger</c>: the matrix product of two 8-bit tensors, each measured from a zero point, summed into
/// 32-bit integers.
/// </summary>
/// <remarks>
/// <para>
/// This is the second half of the dynamic 8-bit path: <c>DynamicQuantizeLinear</c> turns a layer's activations
/// into bytes and a scale, this multiplies those bytes by the weight's bytes, and the <c>Cast</c> and the two
/// <c>Mul</c> nodes that follow turn the integers back into floats by the two scales. Nothing here rounds and
/// nothing approximates - the approximation was made when the numbers became bytes.
/// </para>
/// <para>
/// A CONSTANT WEIGHT IS TAKEN AT LOAD TIME, turned round to [N, K] and kept one byte per element, so the model
/// holds one copy of it and no more. A weight that is computed is turned round at the start of each run
/// instead; the graphs this was written for never do that, but the operator allows it.
/// </para>
/// <para>
/// The zero points are optional in the specification and default to nought. The left one must be a single
/// value; the right one may be a single value or one per column, which is what per-channel quantization
/// produces.
/// </para>
/// </remarks>
internal sealed class OnnxMatMulIntegerKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "MatMulInteger";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 4;

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        OnnxValue weight = context.Initializer(1);
        if (weight == null || weight.Rank != 2 || !IsQuantized(weight.ElementType)) return null;

        OnnxValue zeroPoints = context.Initializer(3);
        if (context.HasInput(3) && zeroPoints == null) return null;

        int reduction = (int)weight.Shape[0];
        int width = (int)weight.Shape[1];
        bool signed = weight.ElementType == OnnxElementType.Int8;
        int[] offsets = ReadZeroPoints(zeroPoints, signed, width, context);
        if (offsets == null) return null;

        sbyte[] values = OnnxIntegerGemm.TransposeToSigned(
            weight.Buffer.Data, signed, reduction, width);
        context.FoldInput(1);
        if (context.HasInput(3)) context.FoldInput(3);
        return new OnnxIntegerWeight(values, offsets, reduction, width);
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue left = context.RequireInput(0);
        if (!IsQuantized(left.ElementType))
        {
            throw context.Fail(
                "it multiplies 8-bit tensors and its left input is "
                + OnnxTensor.Name(left.ElementType) + ".");
        }

        if (left.Rank == 0)
        {
            throw context.Fail("it cannot multiply a scalar.");
        }

        OnnxIntegerWeight weight = context.State as OnnxIntegerWeight ?? Fold(context);
        int reduction = (int)left.Shape[left.Rank - 1];
        if (reduction != weight.Reduction)
        {
            throw context.Fail(
                "its left input " + OnnxShape.Describe(left.Shape) + " does not meet a weight of "
                + weight.Reduction.ToString(CultureInfo.InvariantCulture) + " rows.");
        }

        long[] shape;
        int rows;
        if (left.Rank == 1)
        {
            shape = new long[] { weight.Width };
            rows = 1;
        }
        else
        {
            shape = new long[left.Rank];
            rows = 1;
            for (int i = 0; i < left.Rank - 1; i++)
            {
                shape[i] = left.Shape[i];
                rows = checked(rows * (int)left.Shape[i]);
            }

            shape[left.Rank - 1] = weight.Width;
        }

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Int32, shape);
        if (result.Count == 0) return;

        short[] activations = left.ElementType == OnnxElementType.UInt8
            ? OnnxIntegerGemm.Widen(left.Bytes, left.Count)
            : OnnxIntegerGemm.Widen(left.SignedBytes, left.Count);

        OnnxIntegerGemm.Multiply(
            activations, weight.Values, result.Int32s, ZeroPoint(context, 2), weight.ZeroPoints,
            rows, reduction, weight.Width, context.Settings.Kernel, context.Settings.Threads);
    }

    private static bool IsQuantized(OnnxElementType elementType) =>
        elementType == OnnxElementType.UInt8 || elementType == OnnxElementType.Int8;

    private static OnnxIntegerWeight Fold(OnnxOperatorContext context)
    {
        OnnxValue weight = context.RequireInput(1);
        if (weight.Rank != 2 || !IsQuantized(weight.ElementType))
        {
            throw context.Fail(
                "its weight is " + OnnxTensor.Name(weight.ElementType) + " "
                + OnnxShape.Describe(weight.Shape)
                + ", and this engine multiplies by a two-dimensional 8-bit weight.");
        }

        bool signed = weight.ElementType == OnnxElementType.Int8;
        int reduction = (int)weight.Shape[0];
        int width = (int)weight.Shape[1];
        OnnxValue zeroPoints = context.Input(3);
        int[] offsets = RunZeroPoints(context, zeroPoints, signed, width);

        return new OnnxIntegerWeight(
            OnnxIntegerGemm.TransposeToSigned(weight.Buffer.Data, signed, reduction, width),
            offsets, reduction, width);
    }

    private static int[] ReadZeroPoints(
        OnnxValue zeroPoints, bool signed, int width, OnnxNodeLoadContext context)
    {
        if (zeroPoints == null) return new[] { signed ? 0 : -128 };

        if (!IsQuantized(zeroPoints.ElementType))
        {
            throw context.Refuse(
                "its weight zero point is " + OnnxTensor.Name(zeroPoints.ElementType)
                + " and MatMulInteger measures an 8-bit weight from an 8-bit zero point");
        }

        if (zeroPoints.Count != 1 && zeroPoints.Count != width)
        {
            throw context.Refuse(
                "its weight zero point holds " + zeroPoints.Count.ToString(CultureInfo.InvariantCulture)
                + " values, and this operator takes one for the whole weight or one for each of its "
                + width.ToString(CultureInfo.InvariantCulture) + " columns");
        }

        return Shift(zeroPoints, signed);
    }

    private static int[] RunZeroPoints(
        OnnxOperatorContext context, OnnxValue zeroPoints, bool signed, int width)
    {
        if (zeroPoints == null) return new[] { signed ? 0 : -128 };

        if (!IsQuantized(zeroPoints.ElementType) || (zeroPoints.Count != 1 && zeroPoints.Count != width))
        {
            throw context.Fail("its weight zero point is not an 8-bit value for the weight or for each column.");
        }

        return Shift(zeroPoints, signed);
    }

    private static int[] Shift(OnnxValue zeroPoints, bool signed)
    {
        int[] offsets = new int[zeroPoints.Count];
        for (int i = 0; i < offsets.Length; i++)
        {
            // The weight is stored signed whatever it arrived as, so an unsigned zero point moves with it.
            offsets[i] = signed ? zeroPoints.SignedBytes[i] : zeroPoints.Bytes[i] - 128;
        }

        return offsets;
    }

    private static int ZeroPoint(OnnxOperatorContext context, int index)
    {
        OnnxValue value = context.Input(index);
        if (value == null) return 0;

        if (!IsQuantized(value.ElementType) || value.Count != 1)
        {
            throw context.Fail("its left zero point must be one 8-bit value.");
        }

        return value.ElementType == OnnxElementType.UInt8 ? value.Bytes[0] : value.SignedBytes[0];
    }
}
