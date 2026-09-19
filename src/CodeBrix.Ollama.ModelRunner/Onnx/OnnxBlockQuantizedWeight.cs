namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A <c>MatMulNBits</c> weight exactly as the file stores it: the quantized values still PACKED, a scale for
/// every block of the reduction, and the zero points those blocks are measured from.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING HERE IS EXPANDED, and that is the point of the whole operator. A four-bit weight of a hundred
/// million values occupies fifty million bytes in this object and fifty million bytes in the file; turning it
/// into floats when the model loaded would cost four hundred million and would undo the only reason the graph
/// was quantized. The kernel unpacks a block at a time, inside the loop that is already reading it.
/// </para>
/// <para>
/// The layout is the specification's: <c>B</c> is [N, k_blocks, blob_size] where a blob holds one block's
/// values at <c>bits</c> bits each - for four bits, the first value in the low half of the byte and the second
/// in the high half - <c>scales</c> is [N, k_blocks], and <c>zero_points</c> is either packed the same way as
/// <c>B</c> or one value per block in the scales' own type. Absent, the zero point is 2 to the power
/// <c>bits - 1</c>, which is the middle of the range.
/// </para>
/// </remarks>
internal sealed class OnnxBlockQuantizedWeight
{
    /// <summary>Creates the weight.</summary>
    /// <param name="packed">The quantized values, [n, k_blocks, blob_size].</param>
    /// <param name="scales">One scale per block, [n, k_blocks].</param>
    /// <param name="packedZeroPoints">The packed zero points, or <see langword="null"/>.</param>
    /// <param name="floatZeroPoints">The unpacked zero points, or <see langword="null"/>.</param>
    /// <param name="bias">One value added to each column of the result, or <see langword="null"/>.</param>
    /// <param name="reduction">The length of the reduction, which the node calls K.</param>
    /// <param name="width">The width of the result, which the node calls N.</param>
    /// <param name="bits">How many bits each quantized value keeps.</param>
    /// <param name="blockSize">How many values share one scale.</param>
    internal OnnxBlockQuantizedWeight(
        byte[] packed,
        float[] scales,
        byte[] packedZeroPoints,
        float[] floatZeroPoints,
        float[] bias,
        int reduction,
        int width,
        int bits,
        int blockSize)
    {
        Packed = packed;
        Scales = scales;
        PackedZeroPoints = packedZeroPoints;
        FloatZeroPoints = floatZeroPoints;
        Bias = bias;
        Reduction = reduction;
        Width = width;
        Bits = bits;
        BlockSize = blockSize;
        BlockCount = (reduction + blockSize - 1) / blockSize;
        BlobSize = blockSize * bits / 8;
        ZeroPointBlobSize = ((BlockCount * bits) + 7) / 8;
        DefaultZeroPoint = 1 << (bits - 1);
    }

    /// <summary>The quantized values, [n, k_blocks, blob_size].</summary>
    internal byte[] Packed { get; }

    /// <summary>One scale per block, [n, k_blocks].</summary>
    internal float[] Scales { get; }

    /// <summary>The packed zero points, or <see langword="null"/> when they are floats or absent.</summary>
    internal byte[] PackedZeroPoints { get; }

    /// <summary>The unpacked zero points, or <see langword="null"/> when they are packed or absent.</summary>
    internal float[] FloatZeroPoints { get; }

    /// <summary>One value added to each column of the result, or <see langword="null"/>.</summary>
    internal float[] Bias { get; }

    /// <summary>The length of the reduction, which the node calls K.</summary>
    internal int Reduction { get; }

    /// <summary>The width of the result, which the node calls N.</summary>
    internal int Width { get; }

    /// <summary>How many bits each quantized value keeps.</summary>
    internal int Bits { get; }

    /// <summary>How many values share one scale.</summary>
    internal int BlockSize { get; }

    /// <summary>How many blocks the reduction falls into.</summary>
    internal int BlockCount { get; }

    /// <summary>How many bytes one block of values occupies.</summary>
    internal int BlobSize { get; }

    /// <summary>How many bytes one column's packed zero points occupy.</summary>
    internal int ZeroPointBlobSize { get; }

    /// <summary>The zero point a block is measured from when the node carries none.</summary>
    internal int DefaultZeroPoint { get; }
}
