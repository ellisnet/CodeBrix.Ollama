using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Unsqueeze</c>: inserts dimensions of one, without moving any element.
/// </summary>
/// <remarks>
/// <para>
/// ITS AXES ARE COUNTED AGAINST THE RESULT, NOT THE INPUT. Unsqueezing a tensor of rank two at axis 2 gives a
/// rank of three with the new dimension LAST; counting the same axis against the input would put it second
/// and give a differently shaped tensor of the same size, which then broadcasts against the wrong thing
/// several nodes later. It is the classic error in this operator and the reason this file says so.
/// </para>
/// <para>
/// From opset 13 the axes are an input rather than an attribute. Nothing is copied: the result is another
/// description of the same elements.
/// </para>
/// </remarks>
internal sealed class OnnxUnsqueezeKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Unsqueeze";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue data = context.RequireInput(0);
        long[] axes = OnnxIntegers.Read(context.RequireInput(1), context, "its axes");
        int rank = data.Rank + axes.Length;
        bool[] inserted = new bool[rank];

        for (int i = 0; i < axes.Length; i++)
        {
            long axis = axes[i] < 0 ? axes[i] + rank : axes[i];
            if (axis < 0 || axis >= rank)
            {
                throw context.Fail(
                    "the axis " + axes[i].ToString(CultureInfo.InvariantCulture)
                    + " is outside a result of rank " + rank.ToString(CultureInfo.InvariantCulture) + ".");
            }

            if (inserted[axis])
            {
                throw context.Fail(
                    "the axis " + axis.ToString(CultureInfo.InvariantCulture) + " is named twice.");
            }

            inserted[axis] = true;
        }

        long[] shape = new long[rank];
        int from = 0;
        for (int i = 0; i < rank; i++)
        {
            shape[i] = inserted[i] ? 1 : data.Shape[from++];
        }

        context.SetOutput(0, data.Reshaped(shape));
    }
}
