using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The shared half of <c>ReduceSum</c> and <c>ReduceMean</c>: working out which axes go, what shape is left,
/// and adding the elements up.
/// </summary>
/// <remarks>
/// <para>
/// The case a decoder actually runs - reducing the LAST axis, which is what a root-mean-square normalization
/// does on every layer of every step - is a contiguous run of floats per result, so it is added up with
/// vector accumulators. That is quicker than the general walk and, because several partial sums are carried
/// at once, it is also a little more accurate.
/// </para>
/// <para>
/// Everything else walks the input once, keeping a running offset into the RESULT where an axis being reduced
/// has a stride of nought, so every element of the input lands on the element of the result it belongs to.
/// </para>
/// </remarks>
internal static class OnnxReduction
{
    /// <summary>Works out which axes a reduction removes.</summary>
    /// <param name="axes">The axes as the graph states them, any of which may be negative.</param>
    /// <param name="rank">The rank of the tensor being reduced.</param>
    /// <param name="what">What is being reduced, for the message when an axis is outside the rank.</param>
    /// <returns>One flag per dimension, set where the axis is being reduced.</returns>
    internal static bool[] Selected(ReadOnlySpan<long> axes, int rank, string what)
    {
        bool[] reduced = new bool[rank];
        for (int i = 0; i < axes.Length; i++)
        {
            reduced[OnnxShape.NormalizeAxis(axes[i], rank, what)] = true;
        }

        return reduced;
    }

    /// <summary>Every axis, for the case where a reduction states none and does not mean "do nothing".</summary>
    /// <param name="rank">The rank of the tensor being reduced.</param>
    /// <returns>One flag per dimension, all set.</returns>
    internal static bool[] All(int rank)
    {
        bool[] reduced = new bool[rank];
        for (int i = 0; i < rank; i++) reduced[i] = true;
        return reduced;
    }

    /// <summary>The shape a reduction leaves behind.</summary>
    /// <param name="shape">The shape being reduced.</param>
    /// <param name="reduced">Which axes go.</param>
    /// <param name="keepDimensions">Whether a reduced axis stays as a dimension of one.</param>
    /// <returns>The result's shape.</returns>
    internal static long[] ResultShape(long[] shape, bool[] reduced, bool keepDimensions)
    {
        if (keepDimensions)
        {
            long[] kept = new long[shape.Length];
            for (int i = 0; i < shape.Length; i++) kept[i] = reduced[i] ? 1 : shape[i];
            return kept;
        }

        int rank = 0;
        for (int i = 0; i < shape.Length; i++)
        {
            if (!reduced[i]) rank++;
        }

        long[] dropped = new long[rank];
        int at = 0;
        for (int i = 0; i < shape.Length; i++)
        {
            if (!reduced[i]) dropped[at++] = shape[i];
        }

        return dropped;
    }

    /// <summary>Adds up every element of the input that lands on each element of the result.</summary>
    /// <param name="input">The tensor being reduced, which must be float.</param>
    /// <param name="reduced">Which axes go.</param>
    /// <param name="result">The tensor to fill.</param>
    /// <param name="kind">Which arithmetic path to take.</param>
    /// <returns>How many input elements went into each result element.</returns>
    internal static int Sum(OnnxValue input, bool[] reduced, OnnxValue result, OnnxKernelKind kind)
    {
        float[] source = input.Floats;
        float[] target = result.Floats;
        Array.Clear(target, 0, result.Count);

        int rank = input.Rank;
        int group = 1;
        for (int i = 0; i < rank; i++)
        {
            if (reduced[i]) group = checked(group * (int)input.Shape[i]);
        }

        if (input.Count == 0 || result.Count == 0) return group;

        if (TrailingRun(reduced, rank))
        {
            for (int i = 0; i < result.Count; i++)
            {
                target[i] = RunSum(source, i * group, group, kind);
            }

            return group;
        }

        long[] keptShape = ResultShape(input.Shape, reduced, true);
        int[] keptStrides = OnnxShape.Strides(keptShape);
        int[] strides = new int[rank];
        for (int i = 0; i < rank; i++) strides[i] = reduced[i] ? 0 : keptStrides[i];

        int[] index = new int[rank];
        int offset = 0;
        int inner = rank == 0 ? 1 : (int)input.Shape[rank - 1];
        int innerStride = rank == 0 ? 0 : strides[rank - 1];
        int read = 0;

        while (read < input.Count)
        {
            for (int j = 0; j < inner; j++)
            {
                target[offset + (j * innerStride)] += source[read + j];
            }

            read += inner;
            for (int d = rank - 2; d >= 0; d--)
            {
                index[d]++;
                offset += strides[d];
                if (index[d] < input.Shape[d]) break;

                offset -= strides[d] * (int)input.Shape[d];
                index[d] = 0;
            }
        }

        return group;
    }

    /// <summary>
    /// Adds up every INTEGER element of the input that lands on each element of the result.
    /// </summary>
    /// <remarks>
    /// A decoder's attention mask is a row of int64 ones, and totalling it is how the graph works out how many
    /// positions there are: the shape arithmetic of a whole layer hangs off this one sum. There is no vector
    /// path and no fast case for it, because the tensors it runs on are a handful of elements and the walk is
    /// the same walk the float side takes.
    /// </remarks>
    /// <param name="input">The tensor being reduced, which must be int64 or int32.</param>
    /// <param name="reduced">Which axes go.</param>
    /// <param name="result">The tensor to fill, of the same element type.</param>
    internal static void SumIntegers(OnnxValue input, bool[] reduced, OnnxValue result)
    {
        bool wide = input.ElementType == OnnxElementType.Int64;
        long[] source64 = wide ? input.Int64s : null;
        int[] source32 = wide ? null : input.Int32s;
        long[] target64 = wide ? result.Int64s : null;
        int[] target32 = wide ? null : result.Int32s;

        Array.Clear(result.Buffer.Data, 0, result.Count);
        if (input.Count == 0 || result.Count == 0) return;

        int rank = input.Rank;
        long[] keptShape = ResultShape(input.Shape, reduced, true);
        int[] keptStrides = OnnxShape.Strides(keptShape);
        int[] strides = new int[rank];
        for (int i = 0; i < rank; i++) strides[i] = reduced[i] ? 0 : keptStrides[i];

        int[] index = new int[rank];
        int offset = 0;
        int inner = rank == 0 ? 1 : (int)input.Shape[rank - 1];
        int innerStride = rank == 0 ? 0 : strides[rank - 1];
        int read = 0;

        while (read < input.Count)
        {
            for (int j = 0; j < inner; j++)
            {
                if (wide) target64[offset + (j * innerStride)] += source64[read + j];
                else target32[offset + (j * innerStride)] += source32[read + j];
            }

            read += inner;
            for (int d = rank - 2; d >= 0; d--)
            {
                index[d]++;
                offset += strides[d];
                if (index[d] < input.Shape[d]) break;

                offset -= strides[d] * (int)input.Shape[d];
                index[d] = 0;
            }
        }
    }

    /// <summary>Divides every element of a tensor by a count, which is what turns a sum into a mean.</summary>
    /// <param name="values">The elements.</param>
    /// <param name="count">How many elements to divide by.</param>
    /// <param name="divisor">The count each element was summed over.</param>
    internal static void Divide(float[] values, int count, int divisor)
    {
        float scale = 1f / divisor;
        for (int i = 0; i < count; i++) values[i] *= scale;
    }

    private static bool TrailingRun(bool[] reduced, int rank)
    {
        // The reduced axes are a run at the END of the shape, so each result element is a contiguous run of
        // input elements. Reducing nothing at all counts, and so does reducing everything.
        int first = rank;
        for (int i = 0; i < rank; i++)
        {
            if (reduced[i])
            {
                first = i;
                break;
            }
        }

        for (int i = first; i < rank; i++)
        {
            if (!reduced[i]) return false;
        }

        return true;
    }

    private static float RunSum(float[] values, int offset, int count, OnnxKernelKind kind)
    {
        if (kind == OnnxKernelKind.Scalar)
        {
            float total = 0f;
            for (int i = 0; i < count; i++) total += values[offset + i];
            return total;
        }

        int width = Vector<float>.Count;
        Vector<float> accumulated = Vector<float>.Zero;
        int at = 0;
        for (; at + width <= count; at += width)
        {
            accumulated += new Vector<float>(values, offset + at);
        }

        float sum = Vector.Sum(accumulated);
        for (; at < count; at++) sum += values[offset + at];
        return sum;
    }
}
