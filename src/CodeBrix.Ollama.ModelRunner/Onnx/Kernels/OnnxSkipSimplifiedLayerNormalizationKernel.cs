using System;
using System.Globalization;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/contrib_ops/cpu/skip_layer_norm.cc@v1.30.0

/// <summary>
/// <c>SkipSimplifiedLayerNormalization</c> [com.microsoft]: adds the residual (and an optional bias) to the
/// input, hands that sum out as an output of its own, and root-mean-square normalizes it.
/// </summary>
/// <remarks>
/// <para>
/// THE FOURTH OUTPUT IS NOT OPTIONAL IN PRACTICE. <c>input_skip_bias_sum</c> is the residual stream: the model
/// builders wire it into the next block, so a kernel that produced only the normalized tensor would load and
/// then compute the wrong thing from the second layer on. Both are produced here, and the sum is written
/// BEFORE the normalization reads it, exactly as upstream does.
/// </para>
/// <para>
/// The skip is allowed to be shorter than the input and is then repeated, which is how upstream's
/// <c>offset % skip_size</c> reads; the graphs here supply a skip of the same shape, and the general case
/// costs nothing.
/// </para>
/// <para>
/// <c>mean</c> is the second output and is nought by definition for a simplified normalization - there is no
/// centering term - and upstream writes that nought out rather than leaving the buffer alone, so this does too.
/// </para>
/// </remarks>
internal sealed class OnnxSkipSimplifiedLayerNormalizationKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "SkipSimplifiedLayerNormalization";

    /// <inheritdoc />
    internal override string Domain => OnnxKernels.ContributedDomain;

    /// <inheritdoc />
    internal override int MinInputs => 3;

    /// <inheritdoc />
    internal override int MaxInputs => 4;

    /// <inheritdoc />
    internal override int MinOutputs => 1;

    /// <inheritdoc />
    internal override int MaxOutputs => 4;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "epsilon" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) =>
        new OnnxLayerNormSettings(-1, context.Float("epsilon", 1e-5f));

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxLayerNormSettings settings = (OnnxLayerNormSettings)context.State;
        OnnxValue input = context.RequireInput(0);
        OnnxValue skip = context.RequireInput(1);
        OnnxValue gamma = context.RequireInput(2);
        OnnxValue bias = context.Input(3);

        if (input.ElementType != OnnxElementType.Float
            || skip.ElementType != OnnxElementType.Float
            || gamma.ElementType != OnnxElementType.Float
            || (bias != null && bias.ElementType != OnnxElementType.Float))
        {
            throw context.Fail("it normalizes float tensors.");
        }

        if (input.Rank < 2)
        {
            throw context.Fail(
                "its input " + OnnxShape.Describe(input.Shape)
                + " has fewer than the two dimensions this operator normalizes.");
        }

        int width = (int)input.Shape[input.Rank - 1];
        int rows = width == 0 ? 0 : input.Count / width;

        if (gamma.Count != width || (bias != null && bias.Count != width))
        {
            throw context.Fail(
                "its scale or bias does not hold the "
                + width.ToString(CultureInfo.InvariantCulture) + " elements a row is wide.");
        }

        if (skip.Count == 0 && input.Count != 0)
        {
            throw context.Fail("its skip holds nothing to add.");
        }

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, (long[])input.Shape.Clone());

        long[] statistics = (long[])input.Shape.Clone();
        statistics[statistics.Length - 1] = 1;
        if (context.WantsOutput(1))
        {
            // A simplified normalization has no centering term, so its mean is nought - written out rather
            // than left as whatever the buffer last held.
            Array.Clear(context.AllocateOutput(1, OnnxElementType.Float, (long[])statistics.Clone()).Floats);
        }

        float[] inverseDeviation = null;
        if (context.WantsOutput(2))
        {
            inverseDeviation =
                context.AllocateOutput(2, OnnxElementType.Float, (long[])statistics.Clone()).Floats;
        }

        // The sum is a real output as well as the thing that is normalized, so it is built first and then
        // read; when nothing asks for it the normalization writes over it in place.
        OnnxValue sum = context.WantsOutput(3)
            ? context.AllocateOutput(3, OnnxElementType.Float, (long[])input.Shape.Clone())
            : result;

        if (result.Count == 0) return;

        AddSkip(input, skip, bias, sum.Floats, rows, width, context.Settings.Threads);
        OnnxRootMeanSquareNorm.Normalize(
            sum.Floats, result.Floats, gamma.Floats, inverseDeviation,
            rows, width, settings.Epsilon, context.Settings.Threads);
    }

    private static void AddSkip(
        OnnxValue input, OnnxValue skip, OnnxValue bias, float[] target, int rows, int width, int threads)
    {
        float[] left = input.Floats;
        float[] residual = skip.Floats;
        float[] offsets = bias?.Floats;
        int span = skip.Count;

        int workers = threads > 1 && (long)rows * width >= OnnxGemm.ParallelThreshold
            ? Math.Min(threads, rows)
            : 1;

        if (workers <= 1)
        {
            for (int row = 0; row < rows; row++) AddRow(left, residual, offsets, target, row, width, span);
            return;
        }

        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            for (int row = worker; row < rows; row += workers)
            {
                AddRow(left, residual, offsets, target, row, width, span);
            }
        });
    }

    private static void AddRow(
        float[] left, float[] residual, float[] offsets, float[] target, int row, int width, int span)
    {
        int offset = row * width;
        for (int i = 0; i < width; i++)
        {
            float value = left[offset + i] + residual[(offset + i) % span];
            if (offsets != null) value += offsets[i];
            target[offset + i] = value;
        }
    }
}
