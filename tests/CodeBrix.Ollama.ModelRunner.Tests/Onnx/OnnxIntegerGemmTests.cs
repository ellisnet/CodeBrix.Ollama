using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The 8-bit integer matrix kernel: the wide path against the plain one, and both against arithmetic worked
/// out a different way.
/// </summary>
/// <remarks>
/// This one is held to EXACT equality rather than to a tolerance, because integer arithmetic has no rounding
/// to disagree about: a wide path that differs from the scalar one by a single count is wrong, not
/// approximate.
/// </remarks>
public sealed class OnnxIntegerGemmTests
{
    /// <summary>The wide path and the scalar one produce identical integers.</summary>
    /// <param name="rows">How many rows of input.</param>
    /// <param name="reduction">The length of the reduction.</param>
    /// <param name="width">The width of the result.</param>
    [Theory]
    [InlineData(1, 16, 4)]
    [InlineData(2, 33, 5)]
    [InlineData(3, 64, 8)]
    [InlineData(4, 129, 3)]
    [InlineData(1, 7, 9)]
    [InlineData(2, 256, 16)]
    [InlineData(1, 0, 5)]
    [InlineData(3, 31, 13)]
    [InlineData(3, 1025, 101)]
    public void Multiply_on_the_wide_path_matches_the_scalar_one(int rows, int reduction, int width)
    {
        //Arrange
        var random = new Random(rows + (reduction * 7) + (width * 31));
        var left = new short[rows * reduction];
        for (int i = 0; i < left.Length; i++) left[i] = (short)random.Next(0, 256);

        var packed = new sbyte[width * reduction];
        for (int i = 0; i < packed.Length; i++) packed[i] = (sbyte)random.Next(-128, 128);

        var zeroPoints = new int[width];
        for (int i = 0; i < width; i++) zeroPoints[i] = random.Next(-128, 128);

        //Act
        var wide = new int[rows * width];
        var narrow = new int[rows * width];
        OnnxIntegerGemm.Multiply(left, packed, wide, 137, zeroPoints, rows, reduction, width,
            OnnxKernelKind.Avx2, 1);
        OnnxIntegerGemm.Multiply(left, packed, narrow, 137, zeroPoints, rows, reduction, width,
            OnnxKernelKind.Scalar, 1);

        //Assert
        wide.Should().BeEquivalentTo(narrow);
        wide.Should().BeEquivalentTo(Reference(left, packed, 137, zeroPoints, rows, reduction, width));
    }

    /// <summary>One zero point for the whole weight is the same as the same value repeated per column.</summary>
    [Fact]
    public void Multiply_with_one_zero_point_matches_one_for_every_column()
    {
        //Arrange
        const int Rows = 2;
        const int Reduction = 48;
        const int Width = 6;
        var random = new Random(4242);
        var left = new short[Rows * Reduction];
        for (int i = 0; i < left.Length; i++) left[i] = (short)random.Next(0, 256);
        var packed = new sbyte[Width * Reduction];
        for (int i = 0; i < packed.Length; i++) packed[i] = (sbyte)random.Next(-128, 128);

        var one = new[] { -17 };
        var many = new int[Width];
        for (int i = 0; i < Width; i++) many[i] = -17;

        //Act
        var shared = new int[Rows * Width];
        var perColumn = new int[Rows * Width];
        OnnxIntegerGemm.Multiply(left, packed, shared, 3, one, Rows, Reduction, Width,
            OnnxKernelKind.Avx2, 1);
        OnnxIntegerGemm.Multiply(left, packed, perColumn, 3, many, Rows, Reduction, Width,
            OnnxKernelKind.Avx2, 1);

        //Assert
        shared.Should().BeEquivalentTo(perColumn);
    }

    /// <summary>Spreading the work over threads does not change a single integer.</summary>
    [Fact]
    public void Multiply_over_threads_gives_the_same_integers()
    {
        //Arrange
        const int Rows = 4;
        const int Reduction = 256;
        const int Width = 64;
        var random = new Random(99);
        var left = new short[Rows * Reduction];
        for (int i = 0; i < left.Length; i++) left[i] = (short)random.Next(0, 256);
        var packed = new sbyte[Width * Reduction];
        for (int i = 0; i < packed.Length; i++) packed[i] = (sbyte)random.Next(-128, 128);

        //Act
        var single = new int[Rows * Width];
        var many = new int[Rows * Width];
        OnnxIntegerGemm.Multiply(left, packed, single, 64, new[] { 0 }, Rows, Reduction, Width,
            OnnxKernelKind.Avx2, 1);
        OnnxIntegerGemm.Multiply(left, packed, many, 64, new[] { 0 }, Rows, Reduction, Width,
            OnnxKernelKind.Avx2, 8);

        //Assert
        many.Should().BeEquivalentTo(single);
    }

    /// <summary>Grouped columns preserve full-range products, tails, and uneven worker boundaries.</summary>
    /// <param name="signed">Whether the activation range is signed.</param>
    /// <param name="sharedZeroPoint">Whether every weight column shares a zero point.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Multiply_groups_preserve_extreme_values_and_worker_boundaries(bool signed, bool sharedZeroPoint)
    {
        //Arrange
        const int Rows = 3;
        const int Reduction = 1025;
        const int Width = 101;
        var left = new short[Rows * Reduction];
        for (int i = 0; i < left.Length; i++)
            left[i] = (short)((i % 3 == 0 ? 255 : 0) - (signed ? 128 : 0));
        var packed = new sbyte[Width * Reduction];
        for (int i = 0; i < packed.Length; i++) packed[i] = i % 5 == 0 ? (sbyte)-128 : (sbyte)127;
        var points = new int[Width];
        for (int i = 0; i < Width; i++) points[i] = sharedZeroPoint || (i & 1) == 0 ? -128 : 127;
        var zeroPoints = sharedZeroPoint ? new[] { -128 } : points;
        int leftZeroPoint = signed ? 127 : 255;
        var reference = Reference(left, packed, leftZeroPoint, points, Rows, Reduction, Width);

        //Act and assert
        foreach (var kind in new[] { OnnxKernelKind.Avx2, OnnxKernelKind.Vector, OnnxKernelKind.Scalar })
        {
            var result = new int[Rows * Width];
            OnnxIntegerGemm.Multiply(left, packed, result, leftZeroPoint, zeroPoints,
                Rows, Reduction, Width, kind, 8);
            result.Should().Equal(reference);
        }
    }

    /// <summary>Long reductions retain the specified 32-bit accumulation without saturating products.</summary>
    [Fact]
    public void Multiply_groups_preserve_wrapping_accumulation()
    {
        //Arrange
        const int Reduction = 70001;
        const int Width = 5;
        var left = new short[Reduction];
        Array.Fill(left, (short)255);
        var packed = new sbyte[Width * Reduction];
        Array.Fill(packed, (sbyte)127);
        var result = new int[Width];
        int expected = unchecked((int)((long)Reduction * 255 * 255));

        //Act
        OnnxIntegerGemm.Multiply(left, packed, result, 0, new[] { -128 },
            1, Reduction, Width, OnnxKernelKind.Avx2, 1);

        //Assert
        result.Should().OnlyContain(value => value == expected);
    }

    /// <summary>
    /// An unsigned weight turned into a signed one, with its zero point shifted to match, is the same
    /// arithmetic.
    /// </summary>
    [Fact]
    public void TransposeToSigned_shifts_an_unsigned_weight_by_the_same_amount_as_its_zero_point()
    {
        //Arrange - [k, n] with k = 2 and n = 3
        var unsigned = new byte[] { 0, 1, 200, 255, 128, 7 };

        //Act
        var signed = OnnxIntegerGemm.TransposeToSigned(unsigned, false, 2, 3);

        //Assert - turned round to [n, k], each byte 128 lower
        signed.Should().BeEquivalentTo(new sbyte[] { -128, 127, -127, 0, 72, -121 });
    }

    /// <summary>A signed weight is turned round and left alone.</summary>
    [Fact]
    public void TransposeToSigned_leaves_a_signed_weight_as_it_is()
    {
        //Arrange
        var signed = new sbyte[] { -128, 1, 100, 127, -1, 7 };

        //Act
        var turned = OnnxIntegerGemm.TransposeToSigned(signed, true, 2, 3);

        //Assert
        turned.Should().BeEquivalentTo(new sbyte[] { -128, 127, 1, -1, 100, 7 });
    }

    /// <summary>Widening a run of bytes keeps them unsigned or signed, as the type says.</summary>
    [Fact]
    public void Widen_reads_a_byte_as_its_own_type()
    {
        //Arrange and act
        var unsigned = OnnxIntegerGemm.Widen(new byte[] { 0, 1, 200, 255 }, 4);
        var signed = OnnxIntegerGemm.Widen(new sbyte[] { 0, 1, -56, -1 }, 4);

        //Assert
        unsigned.Should().BeEquivalentTo(new short[] { 0, 1, 200, 255 });
        signed.Should().BeEquivalentTo(new short[] { 0, 1, -56, -1 });
    }

    private static int[] Reference(
        short[] left, sbyte[] packed, int leftZeroPoint, int[] zeroPoints, int rows, int reduction, int width)
    {
        var result = new int[rows * width];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < width; column++)
            {
                long sum = 0;
                for (int k = 0; k < reduction; k++)
                {
                    sum += (long)(left[(row * reduction) + k] - leftZeroPoint)
                        * (packed[(column * reduction) + k] - zeroPoints[column]);
                }

                result[(row * width) + column] = (int)sum;
            }
        }

        return result;
    }
}
