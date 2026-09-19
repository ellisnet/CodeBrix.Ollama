using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Slice</c>: a rectangular piece of a tensor, optionally with a step and optionally backwards.
/// </summary>
/// <remarks>
/// <para>
/// THE CLAMPING IS DONE IN 64-BIT ARITHMETIC AND IN THE ORDER THE SPECIFICATION GIVES. An exporter writes
/// "to the end" as the largest 64-bit integer there is, and a start of "from the end" as a large negative
/// one, so narrowing either before it has been clamped turns a whole tensor into an empty one or worse. A
/// negative bound has the dimension added to it FIRST and is clamped SECOND, and the two directions clamp to
/// different ranges: a forward step to [0, dim] at both ends, a backward step to [0, dim-1] at the start and
/// [-1, dim-1] at the end, where -1 means "past the front".
/// </para>
/// <para>
/// The piece is then read by the shared strided walk, with the step folded into the stride, so a backward
/// slice is a negative stride and needs no separate code.
/// </para>
/// </remarks>
internal sealed class OnnxSliceKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Slice";

    /// <inheritdoc />
    internal override int MinInputs => 3;

    /// <inheritdoc />
    internal override int MaxInputs => 5;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue data = context.RequireInput(0);
        long[] starts = OnnxIntegers.Read(context.RequireInput(1), context, "its starts");
        long[] ends = OnnxIntegers.Read(context.RequireInput(2), context, "its ends");
        OnnxValue axesInput = context.Input(3);
        OnnxValue stepsInput = context.Input(4);

        if (ends.Length != starts.Length)
        {
            throw context.Fail(
                "it names " + starts.Length.ToString(CultureInfo.InvariantCulture) + " starts and "
                + ends.Length.ToString(CultureInfo.InvariantCulture) + " ends.");
        }

        long[] axes;
        if (axesInput != null)
        {
            axes = OnnxIntegers.Read(axesInput, context, "its axes");
            if (axes.Length != starts.Length)
            {
                throw context.Fail("it names a different number of axes than starts.");
            }
        }
        else
        {
            axes = new long[starts.Length];
            for (int i = 0; i < axes.Length; i++) axes[i] = i;
        }

        long[] steps;
        if (stepsInput != null)
        {
            steps = OnnxIntegers.Read(stepsInput, context, "its steps");
            if (steps.Length != starts.Length)
            {
                throw context.Fail("it names a different number of steps than starts.");
            }
        }
        else
        {
            steps = new long[starts.Length];
            for (int i = 0; i < steps.Length; i++) steps[i] = 1;
        }

        int rank = data.Rank;
        int[] own = OnnxShape.Strides(data.Shape);
        long[] shape = (long[])data.Shape.Clone();
        int[] strides = new int[rank];
        for (int d = 0; d < rank; d++) strides[d] = own[d];

        bool[] used = new bool[rank];
        int start = 0;

        for (int i = 0; i < starts.Length; i++)
        {
            int axis = OnnxShape.NormalizeAxis(axes[i], rank, context.Node.Describe());
            if (used[axis])
            {
                throw context.Fail(
                    "it slices the axis " + axis.ToString(CultureInfo.InvariantCulture) + " twice.");
            }

            used[axis] = true;

            long dimension = data.Shape[axis];
            long step = steps[i];
            if (step == 0)
            {
                throw context.Fail("a step of nought would never reach the end of the axis.");
            }

            if (dimension == 0)
            {
                shape[axis] = 0;
                continue;
            }

            long from = Resolve(starts[i], dimension, step > 0 ? 0 : 0, step > 0 ? dimension : dimension - 1);
            long to = Resolve(ends[i], dimension, step > 0 ? 0 : -1, step > 0 ? dimension : dimension - 1);

            long length = Length(from, to, step);
            shape[axis] = length;
            strides[axis] = (int)(own[axis] * step);
            start = checked(start + (int)(from * own[axis]));
        }

        OnnxValue result = context.AllocateOutput(0, data.ElementType, shape);
        if (result.Count == 0) return;

        OnnxDataMovement.Strided(data, start, strides, shape, result);
    }

    private static long Resolve(long bound, long dimension, long least, long most)
    {
        // Pull the value into a range where adding the dimension cannot overflow, THEN add it, THEN clamp:
        // an exporter writes "to the end" as the largest 64-bit integer there is.
        long value = bound;
        if (value > dimension) value = dimension;
        else if (value < -dimension - 1) value = -dimension - 1;

        if (value < 0) value += dimension;

        if (value < least) return least;
        return value > most ? most : value;
    }

    private static long Length(long from, long to, long step)
    {
        long difference = to - from;
        if (step > 0)
        {
            return difference <= 0 ? 0 : (difference + step - 1) / step;
        }

        return difference >= 0 ? 0 : (difference + step + 1) / step;
    }
}
