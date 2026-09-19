namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Where</c>: takes each element from one tensor or the other, according to a boolean condition.
/// </summary>
/// <remarks>
/// All THREE tensors are broadcast against each other, not just the two branches - a condition of shape
/// [1, 1, S, T] choosing between scores of shape [B, H, S, T] and a single number is how an attention mask is
/// applied, and none of the three is expanded first.
/// </remarks>
internal sealed class OnnxWhereKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Where";

    /// <inheritdoc />
    internal override int MinInputs => 3;

    /// <inheritdoc />
    internal override int MaxInputs => 3;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue condition = context.RequireInput(0);
        OnnxValue whenTrue = context.RequireInput(1);
        OnnxValue whenFalse = context.RequireInput(2);

        if (condition.ElementType != OnnxElementType.Bool)
        {
            throw context.Fail(
                "its condition is a " + OnnxTensor.Name(condition.ElementType) + " tensor and must be bool.");
        }

        if (whenTrue.ElementType != whenFalse.ElementType)
        {
            throw context.Fail(
                "its two branches are " + OnnxTensor.Name(whenTrue.ElementType) + " and "
                + OnnxTensor.Name(whenFalse.ElementType) + "; they must carry one type.");
        }

        string what = context.Node.Describe();
        long[] shape = OnnxShape.Broadcast(
            OnnxShape.Broadcast(condition.Shape, whenTrue.Shape, what), whenFalse.Shape, what);
        OnnxValue result = context.AllocateOutput(0, whenTrue.ElementType, shape);
        if (result.Count == 0) return;

        switch (whenTrue.ElementType)
        {
            case OnnxElementType.Float:
                OnnxElementwise.Select<float>(condition, whenTrue, whenFalse, result, shape);
                return;
            case OnnxElementType.Int64:
                OnnxElementwise.Select<long>(condition, whenTrue, whenFalse, result, shape);
                return;
            case OnnxElementType.Int32:
                OnnxElementwise.Select<int>(condition, whenTrue, whenFalse, result, shape);
                return;
            case OnnxElementType.Bool:
                OnnxElementwise.Select<bool>(condition, whenTrue, whenFalse, result, shape);
                return;
            default:
                throw context.Fail(
                    "it cannot select between " + OnnxTensor.Name(whenTrue.ElementType) + " tensors.");
        }
    }
}
