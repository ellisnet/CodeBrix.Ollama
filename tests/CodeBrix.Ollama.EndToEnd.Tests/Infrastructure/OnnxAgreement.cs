using System;
using System.Globalization;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// How far two tensors of a real model's output are apart, and whether they would lead a sampler to the same
/// token.
/// </summary>
/// <remarks>
/// The measure is the largest absolute difference DIVIDED BY the largest magnitude in the tensor - the
/// difference on the scale the tensor is actually on. Element-wise relative difference is meaningless on a
/// vector of several thousand logits, where an element of magnitude 1e-7 beside one of magnitude 20 reads as
/// a large error while the two tensors agree perfectly well.
/// </remarks>
public static class OnnxAgreement
{
    /// <summary>Compares two float tensors of the same shape.</summary>
    /// <param name="expected">What onnxruntime produced.</param>
    /// <param name="actual">What the managed engine produced.</param>
    /// <returns>The largest difference as a fraction of the tensor's own scale.</returns>
    /// <exception cref="InvalidOperationException">The shapes or element types do not match.</exception>
    public static double RelativeDifference(OnnxTensor expected, OnnxTensor actual)
    {
        RequireSameShape(expected, actual);
        if (expected.Count == 0) return 0;

        var left = expected.Floats;
        var right = actual.Floats;
        double largest = 0;
        double magnitude = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            magnitude = Math.Max(magnitude, Math.Abs((double)left[i]));
            largest = Math.Max(largest, Math.Abs((double)left[i] - right[i]));
        }

        return magnitude > 0 ? largest / magnitude : largest;
    }

    /// <summary>
    /// Whether the two tensors put the largest element of every row of their last axis in the same place,
    /// which is the decision a greedy sampler makes.
    /// </summary>
    /// <param name="expected">What onnxruntime produced.</param>
    /// <param name="actual">What the managed engine produced.</param>
    /// <returns>How many rows disagree, and how many there were.</returns>
    public static (int Disagreements, int Rows) Argmax(OnnxTensor expected, OnnxTensor actual)
    {
        RequireSameShape(expected, actual);
        if (expected.Count == 0 || expected.Shape.Count == 0) return (0, 0);

        int width = (int)expected.Shape[expected.Shape.Count - 1];
        if (width == 0) return (0, 0);

        int rows = (int)(expected.Count / width);
        int disagreements = 0;
        for (int row = 0; row < rows; row++)
        {
            if (Largest(expected.Floats, row * width, width) != Largest(actual.Floats, row * width, width))
            {
                disagreements++;
            }
        }

        return (disagreements, rows);
    }

    private static int Largest(float[] values, int start, int width)
    {
        int best = 0;
        float largest = values[start];
        for (int i = 1; i < width; i++)
        {
            if (values[start + i] <= largest) continue;

            largest = values[start + i];
            best = i;
        }

        return best;
    }

    private static void RequireSameShape(OnnxTensor expected, OnnxTensor actual)
    {
        if (expected.ElementType != actual.ElementType)
        {
            throw new InvalidOperationException(
                "Expected a " + expected.ElementType + " tensor and got a " + actual.ElementType + " one.");
        }

        if (expected.Shape.Count != actual.Shape.Count)
        {
            throw new InvalidOperationException("Expected " + expected + " and got " + actual + ".");
        }

        for (int i = 0; i < expected.Shape.Count; i++)
        {
            if (expected.Shape[i] != actual.Shape[i])
            {
                throw new InvalidOperationException("Expected " + expected + " and got " + actual + ".");
            }
        }
    }

    /// <summary>Spells a measurement out for a test's output.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Number(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
}
