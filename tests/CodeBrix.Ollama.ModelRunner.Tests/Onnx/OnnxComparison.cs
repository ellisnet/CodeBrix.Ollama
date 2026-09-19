using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// What two tensors differ by: their shapes, how far apart their numbers are, and where they first disagree.
/// </summary>
/// <remarks>
/// <para>
/// The measure that matters for a float tensor is the largest absolute difference DIVIDED BY the largest
/// magnitude in the tensor - how far apart the two are on the scale the tensor itself is on. Element-wise
/// relative difference is meaningless on a vector of logits, where an element of magnitude 1e-7 next to one
/// of magnitude 20 will read as a large relative error while the two tensors agree perfectly well.
/// </para>
/// <para>
/// Integer and boolean tensors are compared for equality, because there is no such thing as nearly.
/// </para>
/// </remarks>
public sealed class OnnxComparison
{
    private OnnxComparison(
        string shapeDifference, double largestDifference, double relativeToLargest, int mismatches,
        string firstMismatch)
    {
        ShapeDifference = shapeDifference;
        LargestDifference = largestDifference;
        RelativeToLargest = relativeToLargest;
        Mismatches = mismatches;
        FirstMismatch = firstMismatch;
    }

    /// <summary>How the shapes or types differ, or <see langword="null"/> when they do not.</summary>
    public string ShapeDifference { get; }

    /// <summary>The largest absolute difference between matching elements.</summary>
    public double LargestDifference { get; }

    /// <summary>That difference as a fraction of the largest magnitude in the expected tensor.</summary>
    public double RelativeToLargest { get; }

    /// <summary>How many elements differ, for an integer or boolean tensor.</summary>
    public int Mismatches { get; }

    /// <summary>The first element that differs, spelled out, or <see langword="null"/> when none does.</summary>
    public string FirstMismatch { get; }

    /// <summary>Compares what a run produced with what ONNX Runtime produced.</summary>
    /// <param name="expected">The oracle's tensor.</param>
    /// <param name="actual">The engine's tensor.</param>
    /// <returns>The comparison.</returns>
    public static OnnxComparison Compare(OnnxTensor expected, OnnxTensor actual)
    {
        if (expected.ElementType != actual.ElementType)
        {
            return new OnnxComparison(
                "expected " + expected.ElementType + " and got " + actual.ElementType, 0, 0, 0, null);
        }

        if (expected.Shape.Count != actual.Shape.Count)
        {
            return new OnnxComparison("expected " + expected + " and got " + actual, 0, 0, 0, null);
        }

        for (int i = 0; i < expected.Shape.Count; i++)
        {
            if (expected.Shape[i] != actual.Shape[i])
            {
                return new OnnxComparison("expected " + expected + " and got " + actual, 0, 0, 0, null);
            }
        }

        return expected.ElementType switch
        {
            OnnxElementType.Float => Floats(expected, actual),
            OnnxElementType.Int64 => Int64s(expected, actual),
            OnnxElementType.Int32 => Int32s(expected, actual),
            _ => Booleans(expected, actual),
        };
    }

    /// <summary>The position of the largest element of a float tensor, which is what a sampler asks for.</summary>
    /// <param name="tensor">The tensor.</param>
    /// <returns>The position, or -1 when the tensor is empty.</returns>
    public static int Argmax(OnnxTensor tensor)
    {
        float[] values = tensor.Floats;
        int best = -1;
        float largest = float.NegativeInfinity;
        for (int i = 0; i < tensor.Count; i++)
        {
            if (values[i] <= largest) continue;

            largest = values[i];
            best = i;
        }

        return best;
    }

    /// <summary>Returns the numbers, for a failing assertion's message.</summary>
    /// <returns>A one-line description.</returns>
    public override string ToString()
    {
        if (ShapeDifference != null) return "shapes differ: " + ShapeDifference;

        return "largest difference "
            + LargestDifference.ToString("G6", CultureInfo.InvariantCulture)
            + ", relative to the tensor's own scale "
            + RelativeToLargest.ToString("G6", CultureInfo.InvariantCulture)
            + ", " + Mismatches.ToString(CultureInfo.InvariantCulture) + " elements differ"
            + (FirstMismatch == null ? string.Empty : ", first at " + FirstMismatch);
    }

    private static OnnxComparison Floats(OnnxTensor expected, OnnxTensor actual)
    {
        float[] left = expected.Floats;
        float[] right = actual.Floats;
        double largest = 0;
        double magnitude = 0;
        int mismatches = 0;
        string first = null;

        for (int i = 0; i < expected.Count; i++)
        {
            double a = left[i];
            double b = right[i];
            if (double.IsNaN(a) || double.IsNaN(b))
            {
                if (double.IsNaN(a) == double.IsNaN(b)) continue;

                mismatches++;
                first ??= Describe(i, left[i], right[i]);
                continue;
            }

            if (double.IsInfinity(a) || double.IsInfinity(b))
            {
                if (a == b) continue;

                mismatches++;
                first ??= Describe(i, left[i], right[i]);
                continue;
            }

            magnitude = Math.Max(magnitude, Math.Abs(a));
            double difference = Math.Abs(a - b);
            if (difference > largest)
            {
                largest = difference;
                first = Describe(i, left[i], right[i]);
            }
        }

        double relative = magnitude > 0 ? largest / magnitude : largest;
        return new OnnxComparison(null, largest, relative, mismatches, first);
    }

    private static OnnxComparison Int64s(OnnxTensor expected, OnnxTensor actual)
    {
        long[] left = expected.Int64s;
        long[] right = actual.Int64s;
        int mismatches = 0;
        string first = null;
        for (int i = 0; i < expected.Count; i++)
        {
            if (left[i] == right[i]) continue;

            mismatches++;
            first ??= Describe(i, left[i], right[i]);
        }

        return new OnnxComparison(null, 0, 0, mismatches, first);
    }

    private static OnnxComparison Int32s(OnnxTensor expected, OnnxTensor actual)
    {
        int[] left = expected.Int32s;
        int[] right = actual.Int32s;
        int mismatches = 0;
        string first = null;
        for (int i = 0; i < expected.Count; i++)
        {
            if (left[i] == right[i]) continue;

            mismatches++;
            first ??= Describe(i, left[i], right[i]);
        }

        return new OnnxComparison(null, 0, 0, mismatches, first);
    }

    private static OnnxComparison Booleans(OnnxTensor expected, OnnxTensor actual)
    {
        bool[] left = expected.Booleans;
        bool[] right = actual.Booleans;
        int mismatches = 0;
        string first = null;
        for (int i = 0; i < expected.Count; i++)
        {
            if (left[i] == right[i]) continue;

            mismatches++;
            first ??= Describe(i, left[i], right[i]);
        }

        return new OnnxComparison(null, 0, 0, mismatches, first);
    }

    private static string Describe(int index, object expected, object actual) =>
        "[" + index.ToString(CultureInfo.InvariantCulture) + "] expected " + expected + " and got " + actual;
}
