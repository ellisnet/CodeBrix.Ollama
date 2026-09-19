using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/core/providers/cpu/nn/layer_norm_impl.cc@v1.30.0

/// <summary>
/// <c>SimplifiedLayerNormalization</c>: root-mean-square normalization of the trailing axes, scaled per
/// element. It is a runtime's own operator that lives in the DEFAULT domain, which is why it looks like a
/// standard one and is not.
/// </summary>
/// <remarks>
/// <para>
/// No release of the ONNX specification defines this operator, and yet the model builders emit it with no
/// domain at all - so the engine answers for it in the default domain, where the graphs put it. A graph that
/// names it in the contributed domain instead is refused, which is the honest answer: that operator does not
/// exist there either.
/// </para>
/// <para>
/// <c>axis</c> says where the normalized part of the shape starts, counted from the front or, when negative,
/// from the back; everything from there on is one row. <c>stash_type</c> says what precision the statistics
/// are kept in and the only value this engine implements is 1, which is 32-bit float - the precision it
/// computes in throughout.
/// </para>
/// </remarks>
internal sealed class OnnxSimplifiedLayerNormalizationKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "SimplifiedLayerNormalization";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override int MinOutputs => 1;

    /// <inheritdoc />
    internal override int MaxOutputs => 2;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "axis", "epsilon", "stash_type" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        long stashType = context.Int("stash_type", 1);
        if (stashType != 1)
        {
            throw context.Refuse(
                "it asks for stash_type " + stashType.ToString(CultureInfo.InvariantCulture)
                + " and this engine keeps the statistics in 32-bit float, which is stash_type 1");
        }

        return new OnnxLayerNormSettings(context.Int("axis", -1), context.Float("epsilon", 1e-5f));
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxLayerNormSettings settings = (OnnxLayerNormSettings)context.State;
        OnnxValue input = context.RequireInput(0);
        OnnxValue gamma = context.RequireInput(1);

        if (input.ElementType != OnnxElementType.Float || gamma.ElementType != OnnxElementType.Float)
        {
            throw context.Fail("it normalizes float tensors.");
        }

        if (input.Rank == 0)
        {
            throw context.Fail("it cannot normalize a scalar.");
        }

        int axis = OnnxShape.NormalizeAxis(settings.Axis, input.Rank, context.Node.Describe());
        int width = 1;
        for (int i = axis; i < input.Rank; i++) width = checked(width * (int)input.Shape[i]);
        int rows = width == 0 ? 0 : input.Count / width;

        if (gamma.Count != width)
        {
            throw context.Fail(
                "its scale holds " + gamma.Count.ToString(CultureInfo.InvariantCulture)
                + " elements and the normalized part of " + OnnxShape.Describe(input.Shape) + " is "
                + width.ToString(CultureInfo.InvariantCulture) + " wide.");
        }

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, (long[])input.Shape.Clone());
        float[] inverseDeviation = null;
        if (context.WantsOutput(1))
        {
            long[] shape = (long[])input.Shape.Clone();
            for (int i = axis; i < shape.Length; i++) shape[i] = 1;
            inverseDeviation = context.AllocateOutput(1, OnnxElementType.Float, shape).Floats;
        }

        if (result.Count == 0) return;

        OnnxRootMeanSquareNorm.Normalize(
            input.Floats, result.Floats, gamma.Floats, inverseDeviation,
            rows, width, settings.Epsilon, context.Settings.Threads);
    }
}
