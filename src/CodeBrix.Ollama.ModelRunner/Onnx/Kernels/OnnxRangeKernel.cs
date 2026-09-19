using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Range</c>: the numbers from a start, by a step, up to but not including a limit.
/// </summary>
/// <remarks>
/// All three arguments are single numbers and they settle the element type of the result between them. It is
/// how a decoder builds the position indices of the positions it is about to compute, so its length is a
/// dynamic shape like any other and an empty range is an ordinary answer.
/// </remarks>
internal sealed class OnnxRangeKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Range";

    /// <inheritdoc />
    internal override int MinInputs => 3;

    /// <inheritdoc />
    internal override int MaxInputs => 3;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue start = context.RequireInput(0);
        OnnxValue limit = context.RequireInput(1);
        OnnxValue delta = context.RequireInput(2);

        if (start.ElementType != limit.ElementType || start.ElementType != delta.ElementType)
        {
            throw context.Fail("its start, limit and step carry different element types.");
        }

        if (start.Count != 1 || limit.Count != 1 || delta.Count != 1)
        {
            throw context.Fail("its start, limit and step must each be a single number.");
        }

        switch (start.ElementType)
        {
            case OnnxElementType.Float:
            {
                float from = start.Floats[0];
                float step = delta.Floats[0];
                if (step == 0f) throw context.Fail("a step of nought would never reach the limit.");

                int count = Count((limit.Floats[0] - (double)from) / step);
                OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, new long[] { count });
                float[] values = result.Floats;
                for (int i = 0; i < count; i++) values[i] = from + (i * step);
                return;
            }

            case OnnxElementType.Int64:
            {
                long from = start.Int64s[0];
                long step = delta.Int64s[0];
                if (step == 0L) throw context.Fail("a step of nought would never reach the limit.");

                int count = Count((limit.Int64s[0] - (double)from) / step);
                OnnxValue result = context.AllocateOutput(0, OnnxElementType.Int64, new long[] { count });
                long[] values = result.Int64s;
                for (int i = 0; i < count; i++) values[i] = from + (i * step);
                return;
            }

            case OnnxElementType.Int32:
            {
                int from = start.Int32s[0];
                int step = delta.Int32s[0];
                if (step == 0) throw context.Fail("a step of nought would never reach the limit.");

                int count = Count((limit.Int32s[0] - (double)from) / step);
                OnnxValue result = context.AllocateOutput(0, OnnxElementType.Int32, new long[] { count });
                int[] values = result.Int32s;
                for (int i = 0; i < count; i++) values[i] = from + (i * step);
                return;
            }

            default:
                throw context.Fail(
                    "it cannot count in " + OnnxTensor.Name(start.ElementType) + ".");
        }
    }

    private static int Count(double span)
    {
        double count = Math.Ceiling(span);
        if (double.IsNaN(count) || count <= 0) return 0;
        if (count > int.MaxValue)
        {
            throw new InferenceException("A Range of " + count.ToString("F0") + " elements is too large.");
        }

        return (int)count;
    }
}
