using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The three arithmetic paths of the matrix kernels, held against each other and against a plain textbook
/// multiplication done in double precision.
/// </summary>
/// <remarks>
/// This is what makes the engine's correctness on a machine that is NOT this one testable here. The AVX2 path
/// runs only on an x86-64 processor that has AVX2 and fused multiply-add; the portable vector path runs
/// wherever .NET does; the scalar path runs everywhere and has no vector instruction in it at all. All three
/// are exercised on this machine, and the reference they are held to is arithmetic rather than a measurement.
/// </remarks>
public sealed class OnnxGemmTests
{
    /// <summary>How far a float result may be from the double-precision reference, relative to its scale.</summary>
    private const double Tolerance = 2e-6;

    /// <summary>Every path computes the textbook product of a matrix and a vector.</summary>
    /// <param name="path">The arithmetic path, as its number: the type itself is internal to the library.</param>
    [Theory]
    [InlineData((int)OnnxKernelKind.Scalar)]
    [InlineData((int)OnnxKernelKind.Vector)]
    [InlineData((int)OnnxKernelKind.Avx2)]
    public void MultiplyPacked_matches_the_textbook_product(int path)
    {
        //Arrange
        const int Rows = 3;
        const int Reduction = 67;
        const int Width = 41;
        var left = Random(Rows * Reduction, 1);
        var stored = Random(Reduction * Width, 2);
        var packed = new float[stored.Length];
        OnnxGemm.Transpose(stored, packed, Reduction, Width);
        var produced = new float[Rows * Width];

        //Act
        OnnxGemm.MultiplyPacked(left, 0, packed, produced, 0, Rows, Reduction, Width, Kind(path), 1);

        //Assert
        Worst(produced, Reference(left, stored, Rows, Reduction, Width)).Should().BeLessThan(Tolerance);
    }

    /// <summary>Every path computes the textbook product with the right-hand side in the layout the graph stores.</summary>
    /// <param name="path">The arithmetic path, as its number: the type itself is internal to the library.</param>
    [Theory]
    [InlineData((int)OnnxKernelKind.Scalar)]
    [InlineData((int)OnnxKernelKind.Vector)]
    [InlineData((int)OnnxKernelKind.Avx2)]
    public void Multiply_matches_the_textbook_product(int path)
    {
        //Arrange
        const int Rows = 7;
        const int Reduction = 53;
        const int Width = 29;
        var left = Random(Rows * Reduction, 3);
        var right = Random(Reduction * Width, 4);
        var produced = new float[Rows * Width];

        //Act
        OnnxGemm.Multiply(left, 0, right, 0, produced, 0, Rows, Reduction, Width, Kind(path), 1);

        //Assert
        Worst(produced, Reference(left, right, Rows, Reduction, Width)).Should().BeLessThan(Tolerance);
    }

    /// <summary>The two layouts agree with each other, which is what makes turning a weight round safe.</summary>
    [Fact]
    public void MultiplyPacked_agrees_with_Multiply()
    {
        //Arrange
        const int Rows = 5;
        const int Reduction = 96;
        const int Width = 64;
        var left = Random(Rows * Reduction, 5);
        var stored = Random(Reduction * Width, 6);
        var packed = new float[stored.Length];
        OnnxGemm.Transpose(stored, packed, Reduction, Width);
        var byRow = new float[Rows * Width];
        var byColumn = new float[Rows * Width];

        //Act
        OnnxGemm.Multiply(left, 0, stored, 0, byRow, 0, Rows, Reduction, Width, OnnxKernelKind.Avx2, 1);
        OnnxGemm.MultiplyPacked(
            left, 0, packed, byColumn, 0, Rows, Reduction, Width, OnnxKernelKind.Avx2, 1);

        //Assert
        Worst(byColumn, ToDouble(byRow)).Should().BeLessThan(Tolerance);
    }

    /// <summary>Spreading a product over threads does not change it beyond reassociation.</summary>
    [Fact]
    public void MultiplyPacked_at_eight_threads_agrees_with_one()
    {
        //Arrange
        const int Rows = 4;
        const int Reduction = 128;
        const int Width = 256;
        var left = Random(Rows * Reduction, 7);
        var packed = Random(Width * Reduction, 8);
        var single = new float[Rows * Width];
        var many = new float[Rows * Width];

        //Act
        OnnxGemm.MultiplyPacked(left, 0, packed, single, 0, Rows, Reduction, Width, OnnxKernelKind.Avx2, 1);
        OnnxGemm.MultiplyPacked(left, 0, packed, many, 0, Rows, Reduction, Width, OnnxKernelKind.Avx2, 8);

        //Assert
        Worst(many, ToDouble(single)).Should().Be(0d);
    }

    /// <summary>
    /// A prompt's rows give EXACTLY what the same rows give one at a time, on every path.
    /// </summary>
    /// <param name="path">The arithmetic path, as its number: the type itself is internal to the library.</param>
    /// <remarks>
    /// More than one row is worked out column by column, so that a weight row is read once for the whole
    /// prompt rather than once for every output. The order the answers are worked out in changes; the
    /// arithmetic of each answer must not, or a prompt and the tokens after it would disagree in their last
    /// bits and a generation could take a different turn. Bit-for-bit is the bar.
    /// </remarks>
    [Theory]
    [InlineData((int)OnnxKernelKind.Scalar)]
    [InlineData((int)OnnxKernelKind.Vector)]
    [InlineData((int)OnnxKernelKind.Avx2)]
    public void MultiplyPacked_of_many_rows_gives_what_the_rows_give_one_at_a_time(int path)
    {
        //Arrange
        const int Rows = 6;
        const int Reduction = 131;
        const int Width = 37;
        var left = Random(Rows * Reduction, 12);
        var packed = Random(Width * Reduction, 13);
        var together = new float[Rows * Width];

        //Act
        OnnxGemm.MultiplyPacked(
            left, 0, packed, together, 0, Rows, Reduction, Width, Kind(path), 1);

        //Assert
        for (int row = 0; row < Rows; row++)
        {
            var one = new float[Reduction];
            Array.Copy(left, row * Reduction, one, 0, Reduction);
            var alone = new float[Width];
            OnnxGemm.MultiplyPacked(one, 0, packed, alone, 0, 1, Reduction, Width, Kind(path), 1);

            for (int column = 0; column < Width; column++)
            {
                together[(row * Width) + column].Should().Be(alone[column], "row " + row + " column " + column);
            }
        }
    }

    /// <summary>
    /// A prompt with more rows than one cache block still gives EXACTLY what the rows give one at a time.
    /// </summary>
    /// <remarks>
    /// A long prompt takes its rows in blocks, so that the block stays in cache while the whole weight is
    /// walked past it. The reduction here is long enough that a block holds fewer rows than the prompt has,
    /// which is the case the shorter test above does not reach.
    /// </remarks>
    [Fact]
    public void MultiplyPacked_with_more_rows_than_one_block_gives_what_the_rows_give_one_at_a_time()
    {
        //Arrange
        const int Rows = 7;
        const int Reduction = 16384;
        const int Width = 5;
        (OnnxGemm.RowBlockBytes / (Reduction * 4)).Should().BeLessThan(Rows);
        var left = Random(Rows * Reduction, 16);
        var packed = Random(Width * Reduction, 17);
        var together = new float[Rows * Width];

        //Act
        OnnxGemm.MultiplyPacked(
            left, 0, packed, together, 0, Rows, Reduction, Width, OnnxKernelKind.Avx2, 1);

        //Assert
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                together[(row * Width) + column].Should().Be(
                    OnnxGemm.Dot(
                        left, row * Reduction, packed, column * Reduction, Reduction, OnnxKernelKind.Avx2),
                    "row " + row + " column " + column);
            }
        }
    }

    /// <summary>
    /// One row against the whole weight gives EXACTLY the dot product of that row with each weight row.
    /// </summary>
    /// <param name="reduction">The length of the reduction, chosen to leave awkward tails.</param>
    /// <remarks>
    /// The wide path works two weight rows out at a time when there is only one row of input, which is what
    /// generating a token is. Two at a time must be the same arithmetic in the same order as one at a time,
    /// or the odd-numbered columns of a token's logits would differ in their last bits from the even ones.
    /// </remarks>
    [Theory]
    [InlineData(64)]
    [InlineData(96)]
    [InlineData(131)]
    [InlineData(7)]
    public void MultiplyPacked_of_one_row_gives_the_dot_product_of_every_weight_row(int reduction)
    {
        //Arrange - an odd width, so the last column falls outside the pairs
        const int Width = 41;
        var left = Random(reduction, 14);
        var packed = Random(Width * reduction, 15);
        var produced = new float[Width];

        //Act
        OnnxGemm.MultiplyPacked(left, 0, packed, produced, 0, 1, reduction, Width, OnnxKernelKind.Avx2, 1);

        //Assert
        for (int column = 0; column < Width; column++)
        {
            produced[column].Should().Be(
                OnnxGemm.Dot(left, 0, packed, column * reduction, reduction, OnnxKernelKind.Avx2),
                "column " + column);
        }
    }

    /// <summary>A dot product agrees between the three paths, tail elements and all.</summary>
    /// <param name="length">How many elements, chosen to leave awkward tails.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(255)]
    public void Dot_agrees_between_the_paths(int length)
    {
        //Arrange
        var left = Random(length, 9);
        var right = Random(length, 10);
        double reference = 0;
        for (int i = 0; i < length; i++) reference += (double)left[i] * right[i];

        //Act
        var scalar = OnnxGemm.Dot(left, 0, right, 0, length, OnnxKernelKind.Scalar);
        var vector = OnnxGemm.Dot(left, 0, right, 0, length, OnnxKernelKind.Vector);
        var wide = OnnxGemm.Dot(left, 0, right, 0, length, OnnxKernelKind.Avx2);

        //Assert
        var scale = Math.Max(Math.Abs(reference), 1e-6);
        (Math.Abs(scalar - reference) / scale).Should().BeLessThan(Tolerance);
        (Math.Abs(vector - reference) / scale).Should().BeLessThan(Tolerance);
        (Math.Abs(wide - reference) / scale).Should().BeLessThan(Tolerance);
    }

    /// <summary>Turning a matrix round and back again gives what it started as.</summary>
    [Fact]
    public void Transpose_twice_gives_the_original()
    {
        //Arrange
        const int Rows = 37;
        const int Columns = 53;
        var source = Random(Rows * Columns, 11);
        var once = new float[source.Length];
        var back = new float[source.Length];

        //Act
        OnnxGemm.Transpose(source, once, Rows, Columns);
        OnnxGemm.Transpose(once, back, Columns, Rows);

        //Assert
        back.Should().Equal(source);
    }

    /// <summary>A reduction of nothing gives zeros rather than an error.</summary>
    [Fact]
    public void Multiply_with_an_empty_reduction_gives_zeros()
    {
        //Arrange
        var produced = new float[6];
        Array.Fill(produced, 3f);

        //Act
        OnnxGemm.Multiply(
            Array.Empty<float>(), 0, Array.Empty<float>(), 0, produced, 0, 2, 0, 3,
            OnnxKernelKind.Avx2, 1);

        //Assert
        produced.Should().Equal(new float[6]);
    }

    private static OnnxKernelKind Kind(int wanted)
    {
        // A processor without AVX2 and fused multiply-add cannot run that path at all, so on such a machine
        // the case is run on the portable vector path instead. It is still the scalar path it is held to.
        var path = (OnnxKernelKind)wanted;
        return path == OnnxKernelKind.Avx2 && OnnxExecutionSettings.Widest() != OnnxKernelKind.Avx2
            ? OnnxKernelKind.Vector
            : path;
    }

    private static float[] Random(int count, int seed)
    {
        var random = new Random(20260918 + seed);
        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = (float)((random.NextDouble() * 2.0) - 1.0);
        return values;
    }

    private static double[] Reference(float[] left, float[] right, int rows, int reduction, int width)
    {
        var produced = new double[rows * width];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < width; j++)
            {
                double total = 0;
                for (int p = 0; p < reduction; p++)
                {
                    total += (double)left[(i * reduction) + p] * right[(p * width) + j];
                }

                produced[(i * width) + j] = total;
            }
        }

        return produced;
    }

    private static double[] ToDouble(float[] values)
    {
        var produced = new double[values.Length];
        for (int i = 0; i < values.Length; i++) produced[i] = values[i];
        return produced;
    }

    private static double Worst(float[] produced, double[] reference)
    {
        double largest = 0;
        double magnitude = 1e-9;
        for (int i = 0; i < produced.Length; i++)
        {
            magnitude = Math.Max(magnitude, Math.Abs(reference[i]));
            largest = Math.Max(largest, Math.Abs(produced[i] - reference[i]));
        }

        return largest / magnitude;
    }
}
