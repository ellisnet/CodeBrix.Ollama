using System;
using System.Threading.Tasks;

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
        if (outer > 1 && result.Count >= 256 * 1024 && context.Settings.Threads > 1)
        {
            int workers = Math.Min(outer, context.Settings.Threads);
            int chunk = (outer + workers - 1) / workers;
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
                Copy(context, target, block, inner, worker * chunk, Math.Min(outer, (worker + 1) * chunk)));
            return;
        }

        Copy(context, target, block, inner, 0, outer);
    }

    private static void Copy(OnnxOperatorContext context, Array target, long block, int inner, int start, int end)
    {
        OnnxValue first = context.RequireInput(0);
        int axis = OnnxShape.NormalizeAxis((long)context.State, first.Rank, context.Node.Describe());
        for (int o = start; o < end; o++)
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
