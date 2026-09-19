using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>ReduceMean</c>: the average of the elements along one or more axes.
/// </summary>
/// <remarks>
/// <para>
/// WHERE THE AXES COME FROM CHANGED. Up to opset 17 they are an ATTRIBUTE on the node; from opset 18 they are
/// a second INPUT, and a further attribute says whether an empty list means "reduce everything" or "do
/// nothing". A decoder's root-mean-square normalization is written both ways depending on when it was
/// exported, so both are read here: the attribute when the node carries one, the input otherwise.
/// </para>
/// </remarks>
internal sealed class OnnxReduceMeanKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "ReduceMean";

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "axes", "keepdims", "noop_with_empty_axes" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        if (context.HasAttribute("axes") && context.HasInput(1))
        {
            throw context.Refuse("it states its axes both as an attribute and as an input");
        }

        return new long[][]
        {
            context.Ints("axes"),
            new[] { context.Int("keepdims", 1), context.Int("noop_with_empty_axes", 0) },
        };
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        long[][] state = (long[][])context.State;
        long[] axes = state[0];
        bool keepDimensions = state[1][0] != 0;
        bool noopWhenEmpty = state[1][1] != 0;

        OnnxValue input = context.RequireInput(0);
        if (input.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "it averages float tensors and was given " + OnnxTensor.Name(input.ElementType) + ".");
        }

        OnnxValue given = context.Input(1);
        if (given != null) axes = OnnxIntegers.Read(given, context, "its axes");

        if (axes.Length == 0 && noopWhenEmpty)
        {
            context.SetOutput(0, input.Reshaped((long[])input.Shape.Clone()));
            return;
        }

        bool[] reduced = axes.Length == 0
            ? OnnxReduction.All(input.Rank)
            : OnnxReduction.Selected(axes, input.Rank, context.Node.Describe());

        long[] shape = OnnxReduction.ResultShape(input.Shape, reduced, keepDimensions);
        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, shape);
        if (result.Count == 0) return;

        int group = OnnxReduction.Sum(input, reduced, result, context.Settings.Kernel);
        if (group == 0)
        {
            throw context.Fail("it cannot average over an axis of length nought.");
        }

        OnnxReduction.Divide(result.Floats, result.Count, group);
    }
}
