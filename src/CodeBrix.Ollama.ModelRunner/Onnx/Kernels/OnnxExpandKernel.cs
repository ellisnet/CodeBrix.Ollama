namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Expand</c>: stretches a tensor to a larger shape by repeating its dimensions of one.
/// </summary>
/// <remarks>
/// The stated shape is broadcast against the tensor's own BIDIRECTIONALLY rather than simply imposed on it,
/// so a result can be wider than the shape asked for where the tensor itself is wider. That is what the
/// specification says, and it matters for a mask that is expanded against a batch it already carries.
/// </remarks>
internal sealed class OnnxExpandKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Expand";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        long[] wanted = OnnxIntegers.Read(context.RequireInput(1), context, "its shape");
        for (int i = 0; i < wanted.Length; i++)
        {
            if (wanted[i] < 0)
            {
                throw context.Fail("its shape names a negative dimension.");
            }
        }

        long[] shape = OnnxShape.Broadcast(input.Shape, wanted, context.Node.Describe());
        OnnxValue result = context.AllocateOutput(0, input.ElementType, shape);
        if (result.Count == 0) return;

        OnnxDataMovement.Strided(
            input, 0, OnnxShape.BroadcastStrides(input.Shape, shape), shape, result);
    }
}
