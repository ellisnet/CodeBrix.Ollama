using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Shape</c>: the shape of a tensor, as an int64 tensor of its own.
/// </summary>
/// <remarks>
/// This is where a decoder's dynamic shapes come from. Nothing in the engine resolves a symbol before a run:
/// the graph asks a tensor how long it is, does arithmetic on the answer with <c>Gather</c>, <c>Concat</c>
/// and the rest, and hands the result to <c>Reshape</c> or <c>Range</c> - all of it ordinary int64 tensor
/// work, on the same execution plan as everything else.
/// </remarks>
internal sealed class OnnxShapeKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Shape";

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "start", "end" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) => new[]
    {
        context.Int("start", 0),
        context.HasAttribute("end") ? context.Int("end", 0) : long.MaxValue,
    };

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        long[] state = (long[])context.State;
        OnnxValue input = context.RequireInput(0);
        int rank = input.Rank;

        int first = Clamp(state[0], rank);
        int last = state[1] == long.MaxValue ? rank : Clamp(state[1], rank);
        int count = Math.Max(0, last - first);

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Int64, new long[] { count });
        long[] values = result.Int64s;
        for (int i = 0; i < count; i++) values[i] = input.Shape[first + i];
    }

    private static int Clamp(long value, int rank)
    {
        long resolved = value < 0 ? value + rank : value;
        if (resolved < 0) return 0;
        return resolved > rank ? rank : (int)resolved;
    }
}
