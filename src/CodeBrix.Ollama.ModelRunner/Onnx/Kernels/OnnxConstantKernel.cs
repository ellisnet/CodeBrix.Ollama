namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Constant</c>: a tensor written into the graph as an attribute rather than as an initializer.
/// </summary>
/// <remarks>
/// It is read once when the model is loaded and the same tensor is handed out on every run. Nothing copies
/// it, so nothing may write to it either - and nothing does: every kernel writes only into a result it has
/// just been given.
/// </remarks>
internal sealed class OnnxConstantKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Constant";

    /// <inheritdoc />
    internal override int MinInputs => 0;

    /// <inheritdoc />
    internal override int MaxInputs => 0;

    /// <inheritdoc />
    internal override string[] Attributes => new[]
    {
        "value", "value_float", "value_floats", "value_int", "value_ints",
    };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        OnnxValue tensor = context.Tensor("value");
        if (tensor != null) return tensor;

        if (context.HasAttribute("value_float"))
        {
            return OnnxValue.Wrap(
                OnnxElementType.Float, new[] { context.Float("value_float", 0f) }, OnnxShape.Scalar);
        }

        if (context.HasAttribute("value_int"))
        {
            return OnnxValue.Wrap(
                OnnxElementType.Int64, new[] { context.Int("value_int", 0L) }, OnnxShape.Scalar);
        }

        if (context.HasAttribute("value_floats"))
        {
            float[] values = context.Floats("value_floats");
            return OnnxValue.Wrap(OnnxElementType.Float, values, new long[] { values.Length });
        }

        if (context.HasAttribute("value_ints"))
        {
            long[] values = context.Ints("value_ints");
            return OnnxValue.Wrap(OnnxElementType.Int64, values, new long[] { values.Length });
        }

        throw context.Refuse("it carries no value at all");
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue value = (OnnxValue)context.State;
        context.SetOutput(0, value.Reshaped((long[])value.Shape.Clone()));
    }
}
