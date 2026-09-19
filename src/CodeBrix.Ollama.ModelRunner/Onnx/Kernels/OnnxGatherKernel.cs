using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Gather</c>: picks slices out of one axis of a tensor, at the positions another tensor names.
/// </summary>
/// <remarks>
/// <para>
/// THE INDICES' OWN SHAPE GOES INTO THE RESULT, in the place of the axis being gathered. That is what makes a
/// SCALAR index tensor remove the axis altogether, which is how <c>Shape</c> followed by a scalar
/// <c>Gather</c> yields a single dimension for <c>Range</c> or <c>Reshape</c> to work with - the commonest
/// idiom in a graph with dynamic shapes, and one that produces a wrong RANK rather than a wrong number when
/// it is got wrong.
/// </para>
/// <para>
/// A negative index counts from the end of the axis, and an index outside the axis is refused rather than
/// read from somewhere else in the buffer.
/// </para>
/// </remarks>
internal sealed class OnnxGatherKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Gather";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "axis" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) => context.Int("axis", 0);

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue data = context.RequireInput(0);
        OnnxValue indices = context.RequireInput(1);
        if (data.Rank == 0)
        {
            throw context.Fail("it cannot gather from a scalar.");
        }

        int axis = OnnxShape.NormalizeAxis((long)context.State, data.Rank, context.Node.Describe());
        long[] shape = new long[data.Rank - 1 + indices.Rank];
        int at = 0;
        for (int i = 0; i < axis; i++) shape[at++] = data.Shape[i];
        for (int i = 0; i < indices.Rank; i++) shape[at++] = indices.Shape[i];
        for (int i = axis + 1; i < data.Rank; i++) shape[at++] = data.Shape[i];

        OnnxValue result = context.AllocateOutput(0, data.ElementType, shape);
        if (result.Count == 0) return;

        int length = (int)data.Shape[axis];
        int inner = 1;
        for (int i = axis + 1; i < data.Rank; i++) inner = checked(inner * (int)data.Shape[i]);
        int outer = 1;
        for (int i = 0; i < axis; i++) outer = checked(outer * (int)data.Shape[i]);

        long[] positions = OnnxIntegers.Read(indices, context, "its indices");
        Array source = data.Buffer.Data;
        Array target = result.Buffer.Data;

        for (int o = 0; o < outer; o++)
        {
            for (int t = 0; t < positions.Length; t++)
            {
                long position = positions[t] < 0 ? positions[t] + length : positions[t];
                if (position < 0 || position >= length)
                {
                    throw context.Fail(
                        "the index " + positions[t].ToString(CultureInfo.InvariantCulture)
                        + " is outside an axis of length " + length.ToString(CultureInfo.InvariantCulture) + ".");
                }

                Array.Copy(
                    source,
                    (((long)o * length) + position) * inner,
                    target,
                    (((long)o * positions.Length) + t) * inner,
                    inner);
            }
        }
    }
}
