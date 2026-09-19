using System;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/core/providers/cpu/nn/layer_norm_impl.cc@v1.30.0

/// <summary>
/// The root-mean-square normalization both simplified layer normalizations are written in terms of, in the
/// order ONNX Runtime's own kernel does the arithmetic in.
/// </summary>
/// <remarks>
/// <para>
/// THE ORDER OF OPERATIONS IS THE POINT. Upstream sums the squares in one pass, takes
/// <c>sqrt(sum / n + epsilon)</c>, and then writes <c>value / deviation * gamma</c> - a division followed by a
/// multiplication, not a multiplication by a reciprocal. Doing it the other way is algebraically the same and
/// differs in the last bits of every element of every layer, which is exactly the kind of difference that
/// accumulates over twelve layers into a different token. The port keeps upstream's order.
/// </para>
/// <para>
/// "Simplified" means there is no centering term: a plain layer normalization subtracts the mean first, and
/// this one does not, which is why its <c>mean</c> output is nought by definition.
/// </para>
/// </remarks>
internal static class OnnxRootMeanSquareNorm
{
    /// <summary>
    /// Normalizes each row of <paramref name="source"/> in place into <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The values to normalize, laid out as <paramref name="rows"/> runs of <paramref name="width"/>.</param>
    /// <param name="target">Where the normalized values go; it may be the same array as the source.</param>
    /// <param name="gamma">The per-element scale, of <paramref name="width"/> elements.</param>
    /// <param name="inverseDeviation">Where the reciprocal of each row's deviation goes, or <see langword="null"/>.</param>
    /// <param name="rows">How many rows there are.</param>
    /// <param name="width">How many elements each row holds.</param>
    /// <param name="epsilon">What is added inside the square root.</param>
    /// <param name="threads">How many threads to spread the rows over.</param>
    internal static void Normalize(
        float[] source,
        float[] target,
        float[] gamma,
        float[] inverseDeviation,
        int rows,
        int width,
        float epsilon,
        int threads)
    {
        if (rows <= 0 || width <= 0) return;

        int workers = threads > 1 && (long)rows * width >= OnnxGemm.ParallelThreshold
            ? Math.Min(threads, rows)
            : 1;

        if (workers <= 1)
        {
            for (int row = 0; row < rows; row++)
            {
                NormalizeRow(source, target, gamma, inverseDeviation, row, width, epsilon);
            }

            return;
        }

        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            for (int row = worker; row < rows; row += workers)
            {
                NormalizeRow(source, target, gamma, inverseDeviation, row, width, epsilon);
            }
        });
    }

    private static void NormalizeRow(
        float[] source, float[] target, float[] gamma, float[] inverseDeviation,
        int row, int width, float epsilon)
    {
        int offset = row * width;
        float sumOfSquares = 0f;
        for (int i = 0; i < width; i++)
        {
            float value = source[offset + i];
            sumOfSquares += value * value;
        }

        float deviation = MathF.Sqrt((sumOfSquares / width) + epsilon);
        for (int i = 0; i < width; i++)
        {
            target[offset + i] = source[offset + i] / deviation * gamma[i];
        }

        if (inverseDeviation != null) inverseDeviation[row] = 1f / deviation;
    }
}
