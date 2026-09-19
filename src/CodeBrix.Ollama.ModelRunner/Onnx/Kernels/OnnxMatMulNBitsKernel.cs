using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/contrib_ops/cpu/quantization/matmul_nbits.cc@v1.30.0

/// <summary>
/// <c>MatMulNBits</c> [com.microsoft]: a float matrix multiplied by a weight whose values were quantized to
/// four or eight bits in blocks down the reduction, each block with a scale and a zero point of its own.
/// </summary>
/// <remarks>
/// <para>
/// THE WEIGHT STAYS PACKED FOR THE LIFE OF THE MODEL. It is taken into this kernel's own state when the model
/// is loaded, exactly as the file holds it, and the block it is in is turned back into floats inside the loop
/// that multiplies it (plan decision D3). Expanding it would turn a model that was made a quarter of its size
/// back into the size it started at, which is the one thing the reduction was for.
/// </para>
/// <para>
/// <c>accuracy_level</c> IS READ AND THEN IGNORED, and that is deliberate rather than an oversight. It does
/// not describe the weight: it is the lowest precision a runtime may compute the ACTIVATIONS at, where 4 means
/// "you may quantize input A to 8-bit integers as well". This engine keeps A in 32-bit floats, which is more
/// accurate than anything the attribute allows, so honouring it would mean deliberately computing worse
/// numbers. The difference against a runtime that does take the option is what the plan's tolerance for a
/// quantized graph - a largest relative difference under a hundredth, with the same greedy choice - is sized
/// for.
/// </para>
/// <para>
/// What is refused, by name: a bit width other than four or eight; a block size that is not a power of two of
/// at least sixteen; <c>g_idx</c>, which is deprecated and would re-map every block; <c>weight_prepacked</c>,
/// which describes a layout only a graphics runtime writes; and a weight, scale or zero point whose shape does
/// not match what the four attributes say it must be. <c>bias</c> IS implemented - it is one addition per
/// column of the result.
/// </para>
/// </remarks>
internal sealed class OnnxMatMulNBitsKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "MatMulNBits";

    /// <inheritdoc />
    internal override string Domain => OnnxKernels.ContributedDomain;

    /// <inheritdoc />
    internal override int MinInputs => 3;

    /// <inheritdoc />
    internal override int MaxInputs => 6;

    /// <inheritdoc />
    internal override string[] Attributes =>
        new[] { "K", "N", "bits", "block_size", "accuracy_level", "weight_prepacked" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        int reduction = Required(context, "K");
        int width = Required(context, "N");
        int bits = Required(context, "bits");
        int blockSize = Required(context, "block_size");

        if (bits != 4 && bits != 8)
        {
            throw context.Refuse(
                "it quantizes its weight to " + bits.ToString(CultureInfo.InvariantCulture)
                + " bits and this engine implements four and eight");
        }

        if (blockSize < 16 || (blockSize & (blockSize - 1)) != 0)
        {
            throw context.Refuse(
                "its block size is " + blockSize.ToString(CultureInfo.InvariantCulture)
                + " and the operator requires a power of two of at least sixteen");
        }

        if (context.Int("weight_prepacked", 0) != 0)
        {
            throw context.Refuse(
                "it says its weight is prepacked, which describes a layout this engine does not read");
        }

        if (context.HasInput(4))
        {
            throw context.Refuse(
                "it carries the deprecated g_idx input, which re-maps every block to a scale of its own and"
                + " this engine does not implement");
        }

        OnnxValue packed = Require(context, 1, "weight");
        OnnxValue scales = Require(context, 2, "scales");
        OnnxValue zeroPoints = context.HasInput(3) ? Require(context, 3, "zero points") : null;
        OnnxValue bias = context.HasInput(5) ? Require(context, 5, "bias") : null;

        int blockCount = (reduction + blockSize - 1) / blockSize;
        int blobSize = blockSize * bits / 8;
        Expect(context, packed, (long)width * blockCount * blobSize, "weight");
        Expect(context, scales, (long)width * blockCount, "scales");

        if (packed.ElementType != OnnxElementType.UInt8)
        {
            throw context.Refuse(
                "its weight is " + OnnxTensor.Name(packed.ElementType)
                + " and the operator packs the quantized values into unsigned bytes");
        }

        if (scales.ElementType != OnnxElementType.Float)
        {
            throw context.Refuse(
                "its scales are " + OnnxTensor.Name(scales.ElementType)
                + " and this engine computes in 32-bit floats (a 16-bit float scale is widened as the model"
                + " is read, so this is a type it cannot widen)");
        }

        byte[] packedZeroPoints = null;
        float[] floatZeroPoints = null;
        if (zeroPoints != null)
        {
            if (zeroPoints.ElementType == OnnxElementType.UInt8)
            {
                Expect(context, zeroPoints, (long)width * (((blockCount * bits) + 7) / 8), "zero points");
                packedZeroPoints = zeroPoints.Bytes;
            }
            else if (zeroPoints.ElementType == OnnxElementType.Float)
            {
                Expect(context, zeroPoints, (long)width * blockCount, "zero points");
                floatZeroPoints = zeroPoints.Floats;
            }
            else
            {
                throw context.Refuse(
                    "its zero points are " + OnnxTensor.Name(zeroPoints.ElementType)
                    + " and the operator packs them as unsigned bytes or states them in the scales' own type");
            }
        }

        if (bias != null)
        {
            Expect(context, bias, width, "bias");
            if (bias.ElementType != OnnxElementType.Float)
            {
                throw context.Refuse(
                    "its bias is " + OnnxTensor.Name(bias.ElementType) + " and this engine adds floats");
            }
        }

        OnnxBlockQuantizedWeight weight = new OnnxBlockQuantizedWeight(
            packed.Bytes, scales.Floats, packedZeroPoints, floatZeroPoints, bias?.Floats,
            reduction, width, bits, blockSize);

        context.FoldInput(1);
        context.FoldInput(2);
        if (zeroPoints != null) context.FoldInput(3);
        if (bias != null) context.FoldInput(5);
        return weight;
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxBlockQuantizedWeight weight = (OnnxBlockQuantizedWeight)context.State;
        OnnxValue left = context.RequireInput(0);
        if (left.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "it multiplies a float tensor by a quantized weight and was given "
                + OnnxTensor.Name(left.ElementType) + ".");
        }

        if (left.Rank == 0)
        {
            throw context.Fail("it cannot multiply a scalar.");
        }

        int reduction = (int)left.Shape[left.Rank - 1];
        if (reduction != weight.Reduction)
        {
            throw context.Fail(
                "its left input " + OnnxShape.Describe(left.Shape) + " does not meet a weight whose reduction"
                + " is " + weight.Reduction.ToString(CultureInfo.InvariantCulture) + ".");
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

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, shape);
        if (result.Count == 0) return;

        OnnxBlockGemm.Multiply(
            left.Floats, weight, result.Floats, rows, context.Settings.Kernel, context.Settings.Threads);
    }

    private static int Required(OnnxNodeLoadContext context, string name)
    {
        if (!context.HasAttribute(name))
        {
            throw context.Refuse("it does not carry the required attribute '" + name + "'");
        }

        long value = context.Int(name, 0);
        if (value <= 0 || value > int.MaxValue)
        {
            throw context.Refuse(
                "its '" + name + "' attribute is " + value.ToString(CultureInfo.InvariantCulture)
                + ", which is not a size");
        }

        return (int)value;
    }

    private static OnnxValue Require(OnnxNodeLoadContext context, int index, string what)
    {
        OnnxValue value = context.Initializer(index);
        if (value == null)
        {
            throw context.Refuse(
                "its " + what + " is computed while the graph runs, and this engine takes a quantized weight"
                + " into its own state when the model is loaded, so it has to be a constant");
        }

        return value;
    }

    private static void Expect(OnnxNodeLoadContext context, OnnxValue value, long count, string what)
    {
        if (value.Count != count)
        {
            throw context.Refuse(
                "its " + what + " holds " + value.Count.ToString(CultureInfo.InvariantCulture)
                + " values and its K, N, bits and block_size ask for "
                + count.ToString(CultureInfo.InvariantCulture));
        }
    }
}
