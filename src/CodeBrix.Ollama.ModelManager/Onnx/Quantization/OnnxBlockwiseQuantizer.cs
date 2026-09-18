/*++

Copyright (c) Microsoft Corporation. All rights reserved.

Licensed under the MIT License.

Module Name:

    q4_dq.cpp

Abstract:

    This module contains the data structures and implementations
    for blocked int4 quantization and dequantization.

    Int4 block quantization is used to compress weight tensors of large
    language models.

--*/

using System;

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/core/mlas/lib/q4_dq.cpp@v1.30.0

/// <summary>
/// The blockwise quantization kernel the ONNX Runtime quantization tools reach through
/// <c>quantize_matmul_4bits</c> and <c>quantize_matmul_8bits</c>: <c>MlasQuantizeBlockwise</c> with
/// <c>columnwise = true</c>, in the shape the <c>BlockwiseQuantizer</c> template gives it. The source is row major
/// and the quantized data, scales and zero points come out column major, always as unsigned integers.
/// </summary>
/// <remarks>
/// The arithmetic is single precision throughout, exactly as the C++ is, including reading a scale back out of the
/// scale buffer after it was narrowed to the element type. That read-back is what makes half-precision weights come
/// out the same on both engines.
/// </remarks>
internal static class OnnxBlockwiseQuantizer
{
    /// <summary>The number of quantized values that pack into one byte at the given bit width.</summary>
    /// <param name="bits">The bit width, 2, 4 or 8.</param>
    /// <returns>The pack size.</returns>
    internal static int PackSize(int bits) => bits switch
    {
        8 => 1,
        4 => 2,
        2 => 4,
        _ => throw new NotSupportedException(
            $"Only 2, 4 and 8 bit blockwise quantization is supported; {bits} was asked for."),
    };

    /// <summary>The largest value the given bit width can hold, unsigned.</summary>
    /// <param name="bits">The bit width, 2, 4 or 8.</param>
    /// <returns>The maximum quantized value.</returns>
    internal static int MaxValue(int bits) => (1 << bits) - 1;

    /// <summary>The value an unsigned quantized zero point takes when quantization is symmetric.</summary>
    /// <param name="bits">The bit width, 2, 4 or 8.</param>
    /// <returns>The midpoint value.</returns>
    internal static int MidValue(int bits) => 1 << (bits - 1);

    /// <summary>The number of blocks the rows of a matrix fall into.</summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="blockSize">The number of rows in one block.</param>
    /// <returns>The block count.</returns>
    internal static int MetaRows(int rows, int blockSize) => (rows + blockSize - 1) / blockSize;

    /// <summary>The number of bytes one column of the quantized matrix occupies.</summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="blockSize">The number of rows in one block.</param>
    /// <param name="bits">The bit width, 2, 4 or 8.</param>
    /// <returns>The byte count of one quantized column.</returns>
    internal static int QuantizedRows(int rows, int blockSize, int bits) =>
        ((MetaRows(rows, blockSize) * blockSize * bits) + 7) / 8;

    /// <summary>
    /// Quantizes a row-major matrix of shape [rows, columns] blockwise along its columns, writing the packed values,
    /// the scales and, when asymmetric, the zero points into column-major buffers.
    /// </summary>
    /// <param name="destination">The packed values, of length <c>QuantizedRows * columns</c>.</param>
    /// <param name="scales">The per-block scales, of length <c>MetaRows * columns</c>.</param>
    /// <param name="zeroPoints">
    /// The packed per-block zero points, of length <c>ceil(MetaRows / PackSize) * columns</c>, or
    /// <see langword="null"/> for symmetric quantization.
    /// </param>
    /// <param name="source">The matrix to quantize, row major.</param>
    /// <param name="blockSize">The number of rows in one block.</param>
    /// <param name="rows">The number of rows of the matrix.</param>
    /// <param name="columns">The number of columns of the matrix.</param>
    /// <param name="leadingDimension">The distance in elements from one source row to the next.</param>
    /// <param name="halfPrecisionScales">Whether the scales are narrowed to half precision as they are stored.</param>
    /// <param name="bits">The bit width, 2, 4 or 8.</param>
    internal static void QuantizeAndTranspose(
        Span<byte> destination,
        Span<float> scales,
        Span<byte> zeroPoints,
        ReadOnlySpan<float> source,
        int blockSize,
        int rows,
        int columns,
        int leadingDimension,
        bool halfPrecisionScales,
        int bits)
    {
        int packSize = PackSize(bits);
        int maxValue = MaxValue(bits);
        int midValue = MidValue(bits);
        float maxValueFloat = maxValue;
        float fullRange = maxValueFloat;
        float halfRange = midValue;
        int rowBlocks = MetaRows(rows, blockSize);
        int quantizedRows = QuantizedRows(rows, blockSize, bits);
        int threadBlockRows = blockSize * packSize;
        int threadRowBlocks = (rows + threadBlockRows - 1) / threadBlockRows;
        bool symmetric = zeroPoints.IsEmpty;

        int[] zeroPointBytes = new int[packSize];
        int[] packedValues = new int[packSize];

        for (int columnBlock = 0; columnBlock < columns; columnBlock++)
        {
            for (int rowBlock = 0; rowBlock < threadRowBlocks; rowBlock++)
            {
                for (int index = 0; index < packSize; index++)
                {
                    zeroPointBytes[index] = midValue;
                    packedValues[index] = 0;
                }

                int r = rowBlock * threadBlockRows;
                int c = columnBlock;
                int rowEnd = Math.Min(r + threadBlockRows, rows);
                int columnEnd = Math.Min(c + 1, columns);
                int metaRow = r / blockSize;
                int metaColumn = c;

                for (int pack = 0; pack < packSize; pack++)
                {
                    float minimum = float.MaxValue;
                    float maximum = -minimum;
                    int rowStart = r + (pack * blockSize);
                    int blockRowEnd = Math.Min(rowStart + blockSize, rowEnd);
                    for (int i = rowStart; i < blockRowEnd; i++)
                    {
                        for (int j = c; j < columnEnd; j++)
                        {
                            float candidate = source[(i * leadingDimension) + j];
                            if (candidate < minimum)
                            {
                                minimum = candidate;
                            }

                            if (candidate > maximum)
                            {
                                maximum = candidate;
                            }
                        }
                    }

                    if (rowStart < blockRowEnd)
                    {
                        int metaIndex = (metaColumn * rowBlocks) + metaRow + pack;
                        if (symmetric)
                        {
                            scales[metaIndex] = Narrow(RangeToScale(minimum, maximum, halfRange), halfPrecisionScales);
                        }
                        else
                        {
                            scales[metaIndex] = Narrow(
                                RangeToScaleAndZeroPoint(minimum, maximum, fullRange, maxValue, out int zeroPoint),
                                halfPrecisionScales);
                            zeroPointBytes[pack] = zeroPoint;
                        }
                    }
                }

                if (!symmetric)
                {
                    int metaIndex = (metaColumn * ((rowBlocks + packSize - 1) / packSize)) + (metaRow / packSize);
                    zeroPoints[metaIndex] = PackBytes(zeroPointBytes, bits);
                }

                for (int j = c; j < columnEnd; j++)
                {
                    int metaC = j;
                    for (int i = r; i < rowEnd; i += packSize)
                    {
                        for (int l = 0; l < packSize && i + l < rowEnd; l++)
                        {
                            int metaR = (i + l) / blockSize;
                            float scale = scales[(metaC * rowBlocks) + metaR];
                            float reciprocalScale = scale != 0f ? 1f / scale : 0f;
                            int zeroPoint = zeroPointBytes[metaR % packSize];
                            float candidate = source[((i + l) * leadingDimension) + j];
                            float scaled = MathF.Round(
                                (candidate * reciprocalScale) + zeroPoint,
                                MidpointRounding.AwayFromZero);
                            packedValues[l] = (int)(byte)Clamp(scaled, 0f, maxValueFloat);
                        }

                        destination[(j * quantizedRows) + (i / packSize)] = PackBytes(packedValues, bits);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Turns the range of a symmetric block into a scale. The larger half of the quantized space maps to the negative
    /// side, which is why the result carries a minus sign.
    /// </summary>
    /// <param name="minimum">The block's smallest value.</param>
    /// <param name="maximum">The block's largest value.</param>
    /// <param name="halfRange">Half the quantized range.</param>
    /// <returns>The block's scale.</returns>
    internal static float RangeToScale(float minimum, float maximum, float halfRange)
    {
        float chosen = MathF.Abs(maximum) > MathF.Abs(minimum) ? maximum : minimum;
        return -chosen / halfRange;
    }

    /// <summary>Turns the range of an asymmetric block into a scale and an unsigned zero point.</summary>
    /// <param name="minimum">The block's smallest value.</param>
    /// <param name="maximum">The block's largest value.</param>
    /// <param name="fullRange">The whole quantized range.</param>
    /// <param name="maxValue">The largest quantized value.</param>
    /// <param name="zeroPoint">Receives the block's zero point.</param>
    /// <returns>The block's scale.</returns>
    internal static float RangeToScaleAndZeroPoint(
        float minimum,
        float maximum,
        float fullRange,
        int maxValue,
        out int zeroPoint)
    {
        minimum = MathF.Min(minimum, 0f);
        maximum = MathF.Max(maximum, 0f);
        float scale = (maximum - minimum) / fullRange;
        float zeroPointFloat = minimum;
        if (scale != 0f)
        {
            zeroPointFloat = 0f - (minimum / scale);
        }

        if (zeroPointFloat < 0f)
        {
            zeroPoint = 0;
        }
        else if (zeroPointFloat > maxValue)
        {
            zeroPoint = maxValue;
        }
        else
        {
            zeroPoint = (int)(byte)MathF.Round(zeroPointFloat, MidpointRounding.AwayFromZero);
        }

        return scale;
    }

    private static byte PackBytes(int[] values, int bits) => bits switch
    {
        8 => (byte)values[0],
        4 => (byte)((values[0] & 0xF) | (values[1] << 4)),
        2 => (byte)((values[0] & 0x3) | (values[1] << 2) | (values[2] << 4) | (values[3] << 6)),
        _ => throw new NotSupportedException(
            $"Only 2, 4 and 8 bit blockwise quantization is supported; {bits} was asked for."),
    };

    private static float Clamp(float value, float low, float high) =>
        value < low ? low : (high < value ? high : value);

    private static float Narrow(float value, bool halfPrecision) => halfPrecision ? (float)(Half)value : value;
}
