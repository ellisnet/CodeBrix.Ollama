using System;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/core/providers/cpu/quantization/dynamicquantizelinear.cc@v1.30.0

/// <summary>
/// <c>DynamicQuantizeLinear</c>: works out a scale and a zero point from the tensor it is given, and turns its
/// floats into unsigned 8-bit integers with them.
/// </summary>
/// <remarks>
/// <para>
/// THE ROUNDING IS THE WHOLE OPERATOR. The specification says "round to nearest, ties to even" twice - once for
/// the zero point and once for the data - and .NET's <c>MathF.Round</c> does exactly that where the obvious
/// <c>(int)(x + 0.5f)</c> does not: it would send 0.5 to 1 and 2.5 to 3 where the answer is 0 and 2. One
/// element rounded the other way is one count of an integer that the matrix multiply then multiplies by a
/// whole row, so this is not a rounding detail, it is the answer.
/// </para>
/// <para>
/// The arithmetic, in the order upstream does it: the smallest and largest values, each pulled towards nought
/// so that the range always includes it; the scale is that range divided by 255; the zero point is
/// <c>-minimum / scale</c> clamped to the byte range and rounded; and every element is
/// <c>x / scale</c> clamped to the byte range SHIFTED by the zero point, rounded, and then shifted. Clamping
/// before rounding rather than after is upstream's order and gives the same answer, because the bounds are
/// whole numbers.
/// </para>
/// <para>
/// A tensor whose values are all the same - an all-zero tensor is the one that happens - has a range of
/// nought, and dividing by that scale would give infinity for every element. Upstream answers with a scale of
/// one, so the values come out as the zero point and dequantize back to nought, and the port does the same.
/// The specification does not say what should happen there.
/// </para>
/// </remarks>
internal sealed class OnnxDynamicQuantizeLinearKernel : OnnxKernel
{
    private const int QuantizedMinimum = 0;
    private const int QuantizedMaximum = 255;

    /// <inheritdoc />
    internal override string OpType => "DynamicQuantizeLinear";

    /// <inheritdoc />
    internal override int MinOutputs => 3;

    /// <inheritdoc />
    internal override int MaxOutputs => 3;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        if (input.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "it quantizes float tensors and was given " + OnnxTensor.Name(input.ElementType) + ".");
        }

        float[] values = input.Floats;
        int count = input.Count;

        float smallest = 0f;
        float largest = 0f;
        for (int i = 0; i < count; i++)
        {
            float value = values[i];
            if (value < smallest) smallest = value;
            if (value > largest) largest = value;
        }

        float scale = largest == smallest ? 1f : (largest - smallest) / (QuantizedMaximum - QuantizedMinimum);
        float candidate = QuantizedMinimum - (smallest / scale);
        if (candidate < QuantizedMinimum) candidate = QuantizedMinimum;
        if (candidate > QuantizedMaximum) candidate = QuantizedMaximum;
        int zeroPoint = (int)MathF.Round(candidate, MidpointRounding.ToEven);

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.UInt8, (long[])input.Shape.Clone());
        context.AllocateOutput(1, OnnxElementType.Float, Array.Empty<long>()).Floats[0] = scale;
        context.AllocateOutput(2, OnnxElementType.UInt8, Array.Empty<long>()).Bytes[0] = (byte)zeroPoint;

        if (count == 0) return;

        byte[] quantized = result.Bytes;
        float low = QuantizedMinimum - zeroPoint;
        float high = QuantizedMaximum - zeroPoint;
        for (int i = 0; i < count; i++)
        {
            float scaled = values[i] / scale;
            if (scaled < low) scaled = low;
            if (scaled > high) scaled = high;
            quantized[i] = (byte)((int)MathF.Round(scaled, MidpointRounding.ToEven) + zeroPoint);
        }
    }
}
