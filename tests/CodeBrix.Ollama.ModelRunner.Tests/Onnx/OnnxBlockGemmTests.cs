using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The block-quantized matrix kernel: the wide path that takes eight four-bit values out of one 32-bit load
/// against the plain one that takes them out a nibble at a time, and both against arithmetic done in double
/// precision.
/// </summary>
/// <remarks>
/// THE UNPACK IS THE PART THAT CAN BE SILENTLY WRONG. Taking a nibble from the high half of a byte where the
/// low half was meant gives a weight that is still a plausible weight, so a model built on it runs and writes
/// nonsense rather than failing. The values here are deliberately RANDOM - nothing about them is smooth - so
/// a half-swapped unpack cannot come out nearly right, and the three paths are held to each other exactly.
/// </remarks>
public sealed class OnnxBlockGemmTests
{
    /// <summary>The three arithmetic paths agree with double-precision arithmetic on every shape that matters.</summary>
    /// <param name="bits">The bit width.</param>
    /// <param name="blockSize">How many values share a scale.</param>
    /// <param name="reduction">The length of the reduction.</param>
    /// <param name="width">The width of the result.</param>
    /// <param name="rows">How many rows of input.</param>
    /// <param name="zeroPoints">Whether the weight carries zero points.</param>
    [Theory]
    [InlineData(4, 16, 64, 5, 1, true)]
    [InlineData(4, 32, 128, 7, 2, true)]
    [InlineData(4, 32, 128, 7, 2, false)]
    [InlineData(4, 64, 256, 3, 4, true)]
    [InlineData(4, 128, 256, 9, 1, true)]
    [InlineData(4, 256, 512, 2, 3, true)]
    [InlineData(4, 32, 70, 5, 2, true)]
    [InlineData(8, 16, 64, 5, 1, true)]
    [InlineData(8, 32, 128, 7, 2, true)]
    [InlineData(8, 32, 128, 7, 2, false)]
    [InlineData(8, 128, 256, 9, 3, true)]
    [InlineData(8, 32, 70, 5, 2, true)]
    public void Multiply_agrees_with_double_precision_on_every_path(
        int bits, int blockSize, int reduction, int width, int rows, bool zeroPoints)
    {
        //Arrange
        var weight = Build(bits, blockSize, reduction, width, zeroPoints, null);
        var left = Left(rows, reduction, 11);
        var reference = Reference(left, weight, rows);

        //Act and assert
        foreach (var kind in new[] { OnnxKernelKind.Avx2, OnnxKernelKind.Vector, OnnxKernelKind.Scalar })
        {
            var produced = new float[rows * width];
            OnnxBlockGemm.Multiply(left, weight, produced, rows, kind, 1);
            for (int i = 0; i < produced.Length; i++)
            {
                Math.Abs(produced[i] - reference[i]).Should().BeLessThan(
                    1e-3 * (1 + Math.Abs(reference[i])), kind + " at " + i);
            }
        }
    }

    /// <summary>The wide path and the plain one give the SAME answer to within floating-point reassociation.</summary>
    /// <param name="bits">The bit width.</param>
    /// <param name="blockSize">How many values share a scale.</param>
    [Theory]
    [InlineData(4, 16)]
    [InlineData(4, 32)]
    [InlineData(4, 64)]
    [InlineData(4, 128)]
    [InlineData(4, 256)]
    [InlineData(8, 16)]
    [InlineData(8, 32)]
    [InlineData(8, 128)]
    public void Multiply_on_the_wide_path_matches_the_scalar_one(int bits, int blockSize)
    {
        //Arrange
        const int Reduction = 512;
        const int Width = 6;
        const int Rows = 3;
        var weight = Build(bits, blockSize, Reduction, Width, true, null);
        var left = Left(Rows, Reduction, 21);

        //Act
        var wide = new float[Rows * Width];
        var narrow = new float[Rows * Width];
        OnnxBlockGemm.Multiply(left, weight, wide, Rows, OnnxKernelKind.Avx2, 1);
        OnnxBlockGemm.Multiply(left, weight, narrow, Rows, OnnxKernelKind.Scalar, 1);

        //Assert
        for (int i = 0; i < wide.Length; i++)
        {
            ((double)Math.Abs(wide[i] - narrow[i])).Should().BeLessThan(
                1e-3 * (1 + Math.Abs(narrow[i])), "element " + i);
        }
    }

    /// <summary>Spreading the work over threads does not change a single number.</summary>
    [Fact]
    public void Multiply_over_threads_gives_the_same_numbers()
    {
        //Arrange
        var weight = Build(4, 32, 256, 40, true, null);
        var left = Left(4, 256, 31);
        var single = new float[4 * 40];
        var many = new float[4 * 40];

        //Act
        OnnxBlockGemm.Multiply(left, weight, single, 4, OnnxKernelKind.Avx2, 1);
        OnnxBlockGemm.Multiply(left, weight, many, 4, OnnxKernelKind.Avx2, 8);

        //Assert
        many.Should().BeEquivalentTo(single);
    }

    /// <summary>A bias is added to every column of the result, on every path.</summary>
    [Fact]
    public void Multiply_adds_the_bias()
    {
        //Arrange
        const int Width = 5;
        var bias = new float[Width];
        for (int i = 0; i < Width; i++) bias[i] = (i + 1) * 0.5f;
        var without = Build(4, 32, 64, Width, true, null);
        var with = Build(4, 32, 64, Width, true, bias);
        var left = Left(2, 64, 41);

        //Act
        var plain = new float[2 * Width];
        var biased = new float[2 * Width];
        OnnxBlockGemm.Multiply(left, without, plain, 2, OnnxKernelKind.Avx2, 1);
        OnnxBlockGemm.Multiply(left, with, biased, 2, OnnxKernelKind.Avx2, 1);

        //Assert
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                var index = (row * Width) + column;
                ((double)Math.Abs(biased[index] - (plain[index] + bias[column]))).Should()
                    .BeLessThan(1e-4);
            }
        }
    }

    /// <summary>With no zero points at all, the middle of the range is what a block is measured from.</summary>
    /// <param name="bits">The bit width.</param>
    /// <param name="expected">The zero point that implies.</param>
    [Theory]
    [InlineData(4, 8f)]
    [InlineData(8, 128f)]
    public void ZeroPoint_without_any_is_the_middle_of_the_range(int bits, float expected) =>
        OnnxBlockGemm.ZeroPoint(Build(bits, 32, 64, 3, false, null), 1, 1).Should().Be(expected);

    /// <summary>A four-bit zero point is read from the right half of the right byte.</summary>
    [Fact]
    public void ZeroPoint_of_four_bits_is_unpacked_from_its_nibble()
    {
        //Arrange - two columns, four blocks each, so two bytes of zero points per column
        var weight = new OnnxBlockQuantizedWeight(
            new byte[2 * 4 * 16], new float[2 * 4], new byte[] { 0x21, 0x43, 0x65, 0x87 }, null, null,
            128, 2, 4, 32);

        //Act and assert
        OnnxBlockGemm.ZeroPoint(weight, 0, 0).Should().Be(1f);
        OnnxBlockGemm.ZeroPoint(weight, 0, 1).Should().Be(2f);
        OnnxBlockGemm.ZeroPoint(weight, 0, 2).Should().Be(3f);
        OnnxBlockGemm.ZeroPoint(weight, 0, 3).Should().Be(4f);
        OnnxBlockGemm.ZeroPoint(weight, 1, 0).Should().Be(5f);
        OnnxBlockGemm.ZeroPoint(weight, 1, 3).Should().Be(8f);
    }

    /// <summary>
    /// A prompt's rows give EXACTLY what the same rows give one at a time, on every path.
    /// </summary>
    /// <param name="bits">The bit width.</param>
    /// <param name="blockSize">How many values share a scale.</param>
    /// <param name="reduction">The length of the reduction, the last case leaving a part-block.</param>
    /// <remarks>
    /// More than one row unpacks a column ONCE and multiplies every row by it, instead of unpacking it again
    /// for each; one row reads the packed bytes straight into the multiply. The two have to be the same
    /// arithmetic in the same order, not merely close, or a prompt and the tokens after it would disagree in
    /// their last bits and the whole generation could take a different turn. Bit-for-bit is the bar.
    /// </remarks>
    [Theory]
    [InlineData(4, 32, 256)]
    [InlineData(4, 128, 512)]
    [InlineData(4, 16, 64)]
    [InlineData(8, 32, 256)]
    [InlineData(8, 128, 512)]
    [InlineData(4, 32, 70)]
    [InlineData(8, 32, 70)]
    public void Multiply_of_many_rows_gives_what_the_rows_give_one_at_a_time(
        int bits, int blockSize, int reduction)
    {
        //Arrange - every row count on both sides of AmortiseRows, so the crossover itself is covered
        const int Width = 13;
        const int Most = 9;
        var bias = new float[Width];
        for (int i = 0; i < Width; i++) bias[i] = (i + 1) * 0.25f;
        var weight = Build(bits, blockSize, reduction, Width, true, bias);
        var left = Left(Most, reduction, 57);

        //Act and assert
        foreach (var kind in new[] { OnnxKernelKind.Avx2, OnnxKernelKind.Vector, OnnxKernelKind.Scalar })
        {
            for (int rows = 2; rows <= Most; rows++)
            {
                var together = new float[rows * Width];
                OnnxBlockGemm.Multiply(left, weight, together, rows, kind, 1);

                for (int row = 0; row < rows; row++)
                {
                    var one = new float[reduction];
                    Array.Copy(left, row * reduction, one, 0, reduction);
                    var alone = new float[Width];
                    OnnxBlockGemm.Multiply(one, weight, alone, 1, kind, 1);

                    for (int column = 0; column < Width; column++)
                    {
                        together[(row * Width) + column].Should().Be(
                            alone[column], kind + ", " + rows + " rows, at row " + row + " column " + column);
                    }
                }
            }
        }
    }

    /// <summary>Spreading a prompt's rows over threads does not change a single number either.</summary>
    [Fact]
    public void Multiply_of_many_rows_over_threads_gives_the_same_numbers()
    {
        //Arrange
        var weight = Build(4, 32, 256, 96, true, null);
        var left = Left(8, 256, 63);
        var single = new float[8 * 96];
        var many = new float[8 * 96];

        //Act
        OnnxBlockGemm.Multiply(left, weight, single, 8, OnnxKernelKind.Avx2, 1);
        OnnxBlockGemm.Multiply(left, weight, many, 8, OnnxKernelKind.Avx2, 8);

        //Assert
        many.Should().BeEquivalentTo(single);
    }

    /// <summary>
    /// The eight-wide part of a reduction is everything but what the LAST block leaves over.
    /// </summary>
    /// <param name="blockSize">How many values share a scale.</param>
    /// <param name="reduction">The length of the reduction.</param>
    /// <param name="expected">How many leading elements the eight-wide loop covers.</param>
    [Theory]
    [InlineData(32, 256, 256)]
    [InlineData(32, 70, 64)]
    [InlineData(16, 70, 64)]
    [InlineData(128, 768, 768)]
    [InlineData(32, 68, 64)]
    public void WideLength_covers_everything_but_the_last_part_block(
        int blockSize, int reduction, int expected) =>
        OnnxBlockGemm.WideLength(Build(4, blockSize, reduction, 2, false, null)).Should().Be(expected);

    private static OnnxBlockQuantizedWeight Build(
        int bits, int blockSize, int reduction, int width, bool zeroPoints, float[] bias)
    {
        var random = new Random(bits * 1000 + blockSize + reduction + width);
        var blocks = (reduction + blockSize - 1) / blockSize;
        var blobSize = blockSize * bits / 8;
        var packed = new byte[width * blocks * blobSize];
        random.NextBytes(packed);

        var scales = new float[width * blocks];
        for (int i = 0; i < scales.Length; i++) scales[i] = (float)((random.NextDouble() * 0.2) + 0.01);

        byte[] points = null;
        if (zeroPoints)
        {
            points = new byte[width * (((blocks * bits) + 7) / 8)];
            random.NextBytes(points);
        }

        return new OnnxBlockQuantizedWeight(
            packed, scales, points, null, bias, reduction, width, bits, blockSize);
    }

    private static float[] Left(int rows, int reduction, int seed)
    {
        var random = new Random(seed);
        var values = new float[rows * reduction];
        for (int i = 0; i < values.Length; i++) values[i] = (float)((random.NextDouble() * 2.0) - 1.0);
        return values;
    }

    /// <summary>
    /// The same product worked out a different way: every value unpacked by hand and every sum taken in
    /// double precision, so that agreement means the kernel is right rather than that two copies of the same
    /// mistake agree.
    /// </summary>
    private static double[] Reference(float[] left, OnnxBlockQuantizedWeight weight, int rows)
    {
        var result = new double[rows * weight.Width];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < weight.Width; column++)
            {
                double sum = 0;
                for (int k = 0; k < weight.Reduction; k++)
                {
                    var block = k / weight.BlockSize;
                    var inside = k % weight.BlockSize;
                    var blob = ((column * weight.BlockCount) + block) * weight.BlobSize;
                    int quantized;
                    if (weight.Bits == 8)
                    {
                        quantized = weight.Packed[blob + inside];
                    }
                    else
                    {
                        var pair = weight.Packed[blob + (inside / 2)];
                        quantized = (inside % 2) == 0 ? pair & 0x0F : pair >> 4;
                    }

                    var scale = weight.Scales[(column * weight.BlockCount) + block];
                    var zeroPoint = OnnxBlockGemm.ZeroPoint(weight, column, block);
                    sum += (double)left[(row * weight.Reduction) + k] * ((quantized - zeroPoint) * scale);
                }

                if (weight.Bias != null) sum += weight.Bias[column];
                result[(row * weight.Width) + column] = sum;
            }
        }

        return result;
    }
}
