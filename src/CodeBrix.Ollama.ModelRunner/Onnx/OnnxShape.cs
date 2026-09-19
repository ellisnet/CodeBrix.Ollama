using System;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The shape arithmetic every kernel shares: element counts, row-major strides, axis normalization and
/// numpy's bidirectional broadcasting.
/// </summary>
/// <remarks>
/// A dimension of zero is ordinary here. An empty tensor is what a cached decode step feeds a graph, so every
/// helper below has to answer for one - a stride into an empty axis, a broadcast against a zero-length
/// dimension - rather than treat it as a failure.
/// </remarks>
internal static class OnnxShape
{
    /// <summary>The shape of a scalar.</summary>
    internal static readonly long[] Scalar = Array.Empty<long>();

    /// <summary>The number of elements a shape holds.</summary>
    /// <param name="shape">The shape.</param>
    /// <returns>The product of the dimensions, and 1 for a scalar.</returns>
    /// <exception cref="InferenceException">A dimension is negative, or the product is larger than an array can be.</exception>
    internal static int ElementCount(long[] shape)
    {
        long count = 1;
        for (int i = 0; i < shape.Length; i++)
        {
            if (shape[i] < 0)
            {
                throw new InferenceException(
                    "A tensor shape " + Describe(shape) + " has a negative dimension.");
            }

            count *= shape[i];
            if (count == 0) return 0;
            if (count > int.MaxValue)
            {
                throw new InferenceException(
                    "A tensor of shape " + Describe(shape) + " holds more elements than a .NET array can.");
            }
        }

        return (int)count;
    }

    /// <summary>The row-major strides of a shape, in elements.</summary>
    /// <param name="shape">The shape.</param>
    /// <returns>One stride per dimension.</returns>
    internal static int[] Strides(long[] shape)
    {
        int[] strides = new int[shape.Length];
        int running = 1;
        for (int i = shape.Length - 1; i >= 0; i--)
        {
            strides[i] = running;
            running = checked(running * (int)shape[i]);
        }

        return strides;
    }

    /// <summary>
    /// The strides that read a tensor of one shape while walking a larger, broadcast shape: zero wherever the
    /// tensor is stretched, and the tensor's own stride everywhere else.
    /// </summary>
    /// <param name="shape">The tensor's shape.</param>
    /// <param name="target">The broadcast shape, whose rank is at least the tensor's.</param>
    /// <returns>One stride per dimension of <paramref name="target"/>.</returns>
    internal static int[] BroadcastStrides(long[] shape, long[] target)
    {
        int[] own = Strides(shape);
        int[] strides = new int[target.Length];
        int offset = target.Length - shape.Length;
        for (int i = 0; i < target.Length; i++)
        {
            int source = i - offset;
            if (source < 0)
            {
                strides[i] = 0;
                continue;
            }

            // A dimension of one is the stretched one: the walk stays on its single element for the whole
            // length of the broadcast dimension, which is exactly what a stride of zero says.
            strides[i] = shape[source] == 1 ? 0 : own[source];
        }

        return strides;
    }

    /// <summary>numpy's bidirectional broadcast of two shapes.</summary>
    /// <param name="left">The first shape.</param>
    /// <param name="right">The second shape.</param>
    /// <param name="what">What is being broadcast, for the message when it cannot be.</param>
    /// <returns>The broadcast shape.</returns>
    /// <exception cref="InferenceException">The two shapes cannot be broadcast against each other.</exception>
    internal static long[] Broadcast(long[] left, long[] right, string what)
    {
        int rank = Math.Max(left.Length, right.Length);
        long[] result = new long[rank];
        for (int i = 0; i < rank; i++)
        {
            long a = i < rank - left.Length ? 1 : left[i - (rank - left.Length)];
            long b = i < rank - right.Length ? 1 : right[i - (rank - right.Length)];
            if (a == b) result[i] = a;
            else if (a == 1) result[i] = b;
            else if (b == 1) result[i] = a;
            else
            {
                throw new InferenceException(
                    what + ": the shapes " + Describe(left) + " and " + Describe(right)
                    + " cannot be broadcast against each other.");
            }
        }

        return result;
    }

    /// <summary>Turns an axis that may be negative into one that is not.</summary>
    /// <param name="axis">The axis as the graph states it; a negative one counts from the end.</param>
    /// <param name="rank">The rank the axis is against.</param>
    /// <param name="what">What the axis belongs to, for the message when it is outside the rank.</param>
    /// <returns>The axis in the range 0 to <paramref name="rank"/> - 1.</returns>
    /// <exception cref="InferenceException">The axis is outside the rank.</exception>
    internal static int NormalizeAxis(long axis, int rank, string what)
    {
        long resolved = axis < 0 ? axis + rank : axis;
        if (resolved < 0 || resolved >= rank)
        {
            throw new InferenceException(
                what + ": the axis " + axis.ToString(CultureInfo.InvariantCulture)
                + " is outside a tensor of rank " + rank.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return (int)resolved;
    }

    /// <summary>Whether two shapes are the same.</summary>
    /// <param name="left">The first shape.</param>
    /// <param name="right">The second shape.</param>
    /// <returns><see langword="true"/> when they have the same rank and the same dimensions.</returns>
    internal static bool SameShape(long[] left, long[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    /// <summary>Spells a shape for a message.</summary>
    /// <param name="shape">The shape.</param>
    /// <returns>Its dimensions in brackets, for example <c>[1,4,0,256]</c>.</returns>
    internal static string Describe(long[] shape)
    {
        StringBuilder text = new StringBuilder();
        text.Append('[');
        for (int i = 0; i < shape.Length; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(shape[i].ToString(CultureInfo.InvariantCulture));
        }

        text.Append(']');
        return text.ToString();
    }
}
