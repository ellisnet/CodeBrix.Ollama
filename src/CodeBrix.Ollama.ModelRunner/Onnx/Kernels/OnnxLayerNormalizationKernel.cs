using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Standard ONNX LayerNormalization over a contiguous suffix of a float tensor.</summary>
internal sealed class OnnxLayerNormalizationKernel : OnnxKernel
{
    internal override string OpType => "LayerNormalization";
    internal override int MinInputs => 2;
    internal override int MaxInputs => 3;
    internal override int MaxOutputs => 3;
    internal override string[] Attributes => new[] { "axis", "epsilon", "stash_type" };

    internal override object Prepare(OnnxNodeLoadContext context)
    {
        if (context.Opset < 17) throw context.Refuse("LayerNormalization requires ONNX opset 17 or newer.");
        if (context.Int("stash_type", 1) != 1) throw context.Refuse("LayerNormalization supports float stash_type 1.");
        float epsilon = context.Float("epsilon", 1e-5f);
        if (!float.IsFinite(epsilon) || epsilon < 0) throw context.Refuse("LayerNormalization epsilon must be finite and nonnegative.");
        return new OnnxLayerNormSettings(context.Int("axis", -1), epsilon);
    }

    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        OnnxValue scale = context.RequireInput(1);
        OnnxValue bias = context.Input(2);
        if (input.ElementType != OnnxElementType.Float || scale.ElementType != OnnxElementType.Float
            || (bias != null && bias.ElementType != OnnxElementType.Float)) throw context.Fail("LayerNormalization requires float tensors.");
        if (input.Rank == 0) throw context.Fail("LayerNormalization cannot normalize a scalar.");
        var settings = (OnnxLayerNormSettings)context.State;
        int axis = OnnxShape.NormalizeAxis(settings.Axis, input.Rank, context.Node.Describe());
        int width = 1;
        for (int i = axis; i < input.Rank; i++) width = checked(width * (int)input.Shape[i]);
        ValidateBroadcast(scale, input, context);
        if (bias != null) ValidateBroadcast(bias, input, context);
        bool contiguous = IsNormalizedSuffix(scale, input, axis)
            && (bias == null || IsNormalizedSuffix(bias, input, axis));
        int[] scaleStrides = contiguous ? null : OnnxShape.BroadcastStrides(scale.Shape, input.Shape);
        int[] biasStrides = contiguous || bias == null ? null : OnnxShape.BroadcastStrides(bias.Shape, input.Shape);
        float[] target = context.AllocateOutput(0, OnnxElementType.Float, (long[])input.Shape.Clone()).Floats;
        long[] statisticsShape = (long[])input.Shape.Clone();
        for (int i = axis; i < statisticsShape.Length; i++) statisticsShape[i] = 1;
        float[] means = context.WantsOutput(1) ? context.AllocateOutput(1, OnnxElementType.Float, statisticsShape).Floats : null;
        float[] deviations = context.WantsOutput(2) ? context.AllocateOutput(2, OnnxElementType.Float, (long[])statisticsShape.Clone()).Floats : null;
        if (input.Count == 0)
        {
            if (width == 0)
            {
                if (means != null) Array.Fill(means, float.NaN);
                if (deviations != null) Array.Fill(deviations, float.NaN);
            }
            return;
        }
        bool vector = context.Settings.Kernel != OnnxKernelKind.Scalar;
        int lanes = Vector<float>.Count;
        for (int row = 0; row < input.Count / width; row++)
        {
            int start = row * width;
            float sum = 0;
            int i = 0;
            if (vector)
            {
                Vector<float> sums = Vector<float>.Zero;
                for (; i + lanes <= width; i += lanes) sums += new Vector<float>(input.Floats, start + i);
                sum = Vector.Sum(sums);
            }
            for (; i < width; i++) sum += input.Floats[start + i];
            float mean = sum / width;
            float square = 0;
            Vector<float> meanVector = new Vector<float>(mean);
            i = 0;
            if (vector)
            {
                Vector<float> squares = Vector<float>.Zero;
                for (; i + lanes <= width; i += lanes)
                {
                    Vector<float> centered = new Vector<float>(input.Floats, start + i) - meanVector;
                    squares += centered * centered;
                }
                square = Vector.Sum(squares);
            }
            for (; i < width; i++)
            {
                float centered = input.Floats[start + i] - mean;
                square += centered * centered;
            }
            float deviation = MathF.Sqrt(square / width + settings.Epsilon);
            if (means != null) means[row] = mean;
            if (deviations != null) deviations[row] = 1f / deviation;
            i = 0;
            if (vector && contiguous)
            {
                Vector<float> deviationVector = new Vector<float>(deviation);
                for (; i + lanes <= width; i += lanes)
                {
                    Vector<float> normalized = (new Vector<float>(input.Floats, start + i) - meanVector) / deviationVector;
                    Vector<float> result = normalized * new Vector<float>(scale.Floats, i);
                    if (bias != null) result += new Vector<float>(bias.Floats, i);
                    result.CopyTo(target, start + i);
                }
            }
            for (; i < width; i++)
            {
                int scaleOffset = contiguous ? i : BroadcastOffset(start + i, input.Shape, scaleStrides);
                int biasOffset = contiguous || bias == null ? i : BroadcastOffset(start + i, input.Shape, biasStrides);
                target[start + i] = ((input.Floats[start + i] - mean) / deviation) * scale.Floats[scaleOffset]
                    + (bias == null ? 0f : bias.Floats[biasOffset]);
            }
        }
    }

    private static void ValidateBroadcast(OnnxValue value, OnnxValue input, OnnxOperatorContext context)
    {
        if (value.Rank > input.Rank || !OnnxShape.SameShape(input.Shape,
            OnnxShape.Broadcast(value.Shape, input.Shape, context.Node.Describe())))
            throw context.Fail("LayerNormalization scale and bias must broadcast to the input shape.");
    }

    private static bool IsNormalizedSuffix(OnnxValue value, OnnxValue input, int axis)
    {
        int leading = input.Rank - value.Rank;
        for (int i = 0; i < input.Rank; i++)
        {
            long dimension = i < leading ? 1 : value.Shape[i - leading];
            if (dimension != (i < axis ? 1 : input.Shape[i])) return false;
        }
        return true;
    }

    private static int BroadcastOffset(int index, long[] shape, int[] strides)
    {
        int offset = 0;
        for (int i = shape.Length - 1; i >= 0; i--)
        {
            offset += (index % (int)shape[i]) * strides[i];
            index /= (int)shape[i];
        }
        return offset;
    }
}
