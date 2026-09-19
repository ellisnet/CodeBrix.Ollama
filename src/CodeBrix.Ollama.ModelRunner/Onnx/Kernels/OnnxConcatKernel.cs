using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Concat</c>: joins several tensors end to end along one axis.
/// </summary>
/// <remarks>
/// A piece of length nought is ordinary and not a special case: a decoder's first cached step concatenates an
/// empty past onto a new key, and every step after it concatenates a new key of length one onto a long past.
/// Both go through the same block copies.
/// </remarks>
internal sealed class OnnxConcatKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Concat";

    /// <inheritdoc />
    internal override int MaxInputs => int.MaxValue;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "axis" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        if (!context.HasAttribute("axis"))
        {
            throw context.Refuse("it states no axis, which Concat requires");
        }

        return context.Int("axis", 0);
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue first = context.RequireInput(0);
        int rank = first.Rank;
        if (rank == 0)
        {
            throw context.Fail("it cannot join scalars.");
        }

        int axis = OnnxShape.NormalizeAxis((long)context.State, rank, context.Node.Describe());
        long joined = 0;
        for (int i = 0; i < context.InputCount; i++)
        {
            OnnxValue piece = context.RequireInput(i);
            if (piece.ElementType != first.ElementType)
            {
                throw context.Fail(
                    "its inputs carry different element types, " + OnnxTensor.Name(first.ElementType)
                    + " and " + OnnxTensor.Name(piece.ElementType) + ".");
            }

            if (piece.Rank != rank)
            {
                throw context.Fail(
                    "its inputs have different ranks, " + OnnxShape.Describe(first.Shape)
                    + " and " + OnnxShape.Describe(piece.Shape) + ".");
            }

            for (int d = 0; d < rank; d++)
            {
                if (d != axis && piece.Shape[d] != first.Shape[d])
                {
                    throw context.Fail(
                        "its inputs " + OnnxShape.Describe(first.Shape) + " and "
                        + OnnxShape.Describe(piece.Shape) + " differ away from the axis they are joined on.");
                }
            }

            joined += piece.Shape[axis];
        }

        long[] shape = (long[])first.Shape.Clone();
        shape[axis] = joined;
        OnnxValue result = context.AllocateOutput(0, first.ElementType, shape);
        if (result.Count == 0) return;

        int inner = 1;
        for (int d = axis + 1; d < rank; d++) inner = checked(inner * (int)first.Shape[d]);
        int outer = 1;
        for (int d = 0; d < axis; d++) outer = checked(outer * (int)first.Shape[d]);

        Array target = result.Buffer.Data;
        long block = joined * inner;
        for (int o = 0; o < outer; o++)
        {
            long at = o * block;
            for (int i = 0; i < context.InputCount; i++)
            {
                OnnxValue piece = context.RequireInput(i);
                long length = piece.Shape[axis] * inner;
                if (length == 0) continue;

                Array.Copy(piece.Buffer.Data, o * length, target, at, length);
                at += length;
            }
        }
    }
}
