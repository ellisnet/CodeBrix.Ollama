namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>ReduceSum</c>: the total of the elements along one or more axes.
/// </summary>
/// <remarks>
/// From opset 13 the axes are an INPUT rather than an attribute, and <c>noop_with_empty_axes</c> settles what
/// an absent or empty list means: nought, the default, reduces EVERY axis, while one makes the node a
/// pass-through. A graph that sums a whole tensor down to a scalar states no axes at all and relies on that
/// default, so getting it the wrong way round turns a number into a tensor.
/// </remarks>
internal sealed class OnnxReduceSumKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "ReduceSum";

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "keepdims", "noop_with_empty_axes" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) =>
        new[] { context.Int("keepdims", 1), context.Int("noop_with_empty_axes", 0) };

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        long[] state = (long[])context.State;
        bool keepDimensions = state[0] != 0;
        bool noopWhenEmpty = state[1] != 0;

        OnnxValue input = context.RequireInput(0);
        if (input.ElementType != OnnxElementType.Float
            && input.ElementType != OnnxElementType.Int64
            && input.ElementType != OnnxElementType.Int32)
        {
            throw context.Fail(
                "it totals float, int64 and int32 tensors and was given "
                + OnnxTensor.Name(input.ElementType) + ".");
        }

        OnnxValue given = context.Input(1);
        long[] axes = given == null ? System.Array.Empty<long>() : OnnxIntegers.Read(given, context, "its axes");

        if (axes.Length == 0 && noopWhenEmpty)
        {
            context.SetOutput(0, input.Reshaped((long[])input.Shape.Clone()));
            return;
        }

        bool[] reduced = axes.Length == 0
            ? OnnxReduction.All(input.Rank)
            : OnnxReduction.Selected(axes, input.Rank, context.Node.Describe());

        long[] shape = OnnxReduction.ResultShape(input.Shape, reduced, keepDimensions);
        OnnxValue result = context.AllocateOutput(0, input.ElementType, shape);
        if (result.Count == 0) return;

        if (input.ElementType == OnnxElementType.Float)
        {
            OnnxReduction.Sum(input, reduced, result, context.Settings.Kernel);
            return;
        }

        OnnxReduction.SumIntegers(input, reduced, result);
    }
}
