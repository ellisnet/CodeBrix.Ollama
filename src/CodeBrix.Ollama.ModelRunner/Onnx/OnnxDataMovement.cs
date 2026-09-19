using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The one routine that <c>Transpose</c>, <c>Slice</c> and <c>Expand</c> are all written in terms of: read
/// the result in order, walking the source with a stride per result dimension.
/// </summary>
/// <remarks>
/// <para>
/// The three operators differ only in what those strides are. A transposition permutes the source's own
/// strides; a slice multiplies them by its steps and starts at an offset; an expansion sets the stride of a
/// stretched dimension to nought. Writing them once means the awkward parts - an empty result, a rank of
/// nought, a negative step - are got right once as well.
/// </para>
/// <para>
/// When the innermost stride is one, which it is for a transposition that leaves the last axis alone and for
/// every ordinary slice, the inner run is a block copy rather than a loop.
/// </para>
/// </remarks>
internal static class OnnxDataMovement
{
    /// <summary>Fills a result by walking a source with a stride per result dimension.</summary>
    /// <param name="source">The tensor to read.</param>
    /// <param name="start">Where in the source the first result element comes from.</param>
    /// <param name="strides">One source stride per result dimension, which may be nought or negative.</param>
    /// <param name="shape">The result's shape.</param>
    /// <param name="result">The tensor to fill.</param>
    internal static void Strided(OnnxValue source, int start, int[] strides, long[] shape, OnnxValue result)
    {
        switch (source.ElementType)
        {
            case OnnxElementType.Float:
                Walk(source.Floats, start, strides, shape, result.Floats, result.Count);
                return;
            case OnnxElementType.Int64:
                Walk(source.Int64s, start, strides, shape, result.Int64s, result.Count);
                return;
            case OnnxElementType.Int32:
                Walk(source.Int32s, start, strides, shape, result.Int32s, result.Count);
                return;
            case OnnxElementType.Bool:
                Walk(source.Booleans, start, strides, shape, result.Booleans, result.Count);
                return;
            default:
                throw new InferenceException(
                    "This engine does not move " + OnnxTensor.Name(source.ElementType) + " tensors about.");
        }
    }

    private static void Walk<T>(T[] source, int start, int[] strides, long[] shape, T[] target, int total)
    {
        if (total == 0) return;

        int rank = shape.Length;
        if (rank == 0)
        {
            target[0] = source[start];
            return;
        }

        int inner = (int)shape[rank - 1];
        int innerStride = strides[rank - 1];
        int[] index = new int[rank];
        int offset = start;
        int written = 0;

        while (written < total)
        {
            if (innerStride == 1)
            {
                Array.Copy(source, offset, target, written, inner);
            }
            else
            {
                for (int j = 0; j < inner; j++) target[written + j] = source[offset + (j * innerStride)];
            }

            written += inner;
            for (int d = rank - 2; d >= 0; d--)
            {
                index[d]++;
                offset += strides[d];
                if (index[d] < shape[d]) break;

                offset -= strides[d] * (int)shape[d];
                index[d] = 0;
            }
        }
    }
}
