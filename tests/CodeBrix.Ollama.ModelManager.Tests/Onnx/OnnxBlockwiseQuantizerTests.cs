using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Pins the constants and the arithmetic the ported blockwise kernel stands on: the traits of each bit width, the
/// block and buffer shapes, and the two range-to-scale formulas, symmetric and asymmetric.
/// </summary>
public sealed class OnnxBlockwiseQuantizerTests
{
    [Theory]
    [InlineData(2, 4, 3, 2)]
    [InlineData(4, 2, 15, 8)]
    [InlineData(8, 1, 255, 128)]
    public void PackSize_pins_the_traits_of_each_bit_width(int bits, int packSize, int maximum, int middle)
    {
        //Act and assert
        OnnxBlockwiseQuantizer.PackSize(bits).Should().Be(packSize);
        OnnxBlockwiseQuantizer.MaxValue(bits).Should().Be(maximum);
        OnnxBlockwiseQuantizer.MidValue(bits).Should().Be(middle);
    }

    [Fact]
    public void PackSize_refuses_a_bit_width_the_kernel_does_not_pack()
    {
        //Arrange
        Action act = () => OnnxBlockwiseQuantizer.PackSize(3);

        //Act and assert
        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(64, 32, 2)]
    [InlineData(70, 32, 3)]
    [InlineData(33, 32, 2)]
    [InlineData(130, 128, 2)]
    [InlineData(64, 128, 1)]
    public void MetaRows_rounds_the_block_count_up(int rows, int blockSize, int expected) =>
        OnnxBlockwiseQuantizer.MetaRows(rows, blockSize).Should().Be(expected);

    [Theory]
    [InlineData(64, 32, 4, 32)]
    [InlineData(70, 32, 4, 48)]
    [InlineData(64, 32, 8, 64)]
    [InlineData(130, 128, 4, 128)]
    public void QuantizedRows_pins_the_packed_bytes_of_one_column(
        int rows,
        int blockSize,
        int bits,
        int expected) =>
        OnnxBlockwiseQuantizer.QuantizedRows(rows, blockSize, bits).Should().Be(expected);

    [Fact]
    public void RangeToScale_maps_the_larger_half_of_the_quantized_space_to_the_negative_side() =>
        OnnxBlockwiseQuantizer.RangeToScale(-1.0f, 0.5f, 8f).Should().Be(0.125f);

    [Fact]
    public void RangeToScale_takes_the_larger_magnitude_of_the_two_ends() =>
        OnnxBlockwiseQuantizer.RangeToScale(-0.5f, 2.0f, 8f).Should().Be(-0.25f);

    [Fact]
    public void RangeToScaleAndZeroPoint_widens_the_range_to_include_zero()
    {
        //Act
        float scale = OnnxBlockwiseQuantizer.RangeToScaleAndZeroPoint(0.5f, 2.0f, 15f, 15, out int zeroPoint);

        //Assert
        scale.Should().Be(2.0f / 15f);
        zeroPoint.Should().Be(0);
    }

    [Fact]
    public void RangeToScaleAndZeroPoint_clamps_a_zero_point_above_the_largest_value()
    {
        //Act
        float scale = OnnxBlockwiseQuantizer.RangeToScaleAndZeroPoint(-2.0f, -0.5f, 15f, 15, out int zeroPoint);

        //Assert
        scale.Should().Be(2.0f / 15f);
        zeroPoint.Should().Be(15);
    }

    [Fact]
    public void RangeToScaleAndZeroPoint_gives_a_flat_block_a_scale_of_zero()
    {
        //Act
        float scale = OnnxBlockwiseQuantizer.RangeToScaleAndZeroPoint(0f, 0f, 15f, 15, out int zeroPoint);

        //Assert
        scale.Should().Be(0f);
        zeroPoint.Should().Be(0);
    }

    [Fact]
    public void QuantizeAndTranspose_writes_the_column_major_layout_the_kernel_describes()
    {
        //Arrange
        float[] source = { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f };
        byte[] destination = new byte[4];
        float[] scales = new float[2];
        byte[] zeroPoints = new byte[2];

        //Act
        OnnxBlockwiseQuantizer.QuantizeAndTranspose(
            destination,
            scales,
            zeroPoints,
            source,
            4,
            4,
            2,
            2,
            false,
            4);

        //Assert
        scales.Should().HaveCount(2);
        destination.Should().HaveCount(4);
        zeroPoints.Should().HaveCount(2);
        scales[0].Should().Be(6f / 15f);
        scales[1].Should().Be(7f / 15f);
    }

    [Fact]
    public void QuantizeAndTranspose_narrows_a_half_precision_scale_before_it_is_used()
    {
        //Arrange
        float[] source = { 0.1f, 0.2f, 0.3f, 0.4f };
        byte[] destination = new byte[2];
        float[] single = new float[1];
        float[] half = new float[1];

        //Act
        OnnxBlockwiseQuantizer.QuantizeAndTranspose(
            destination, single, Span<byte>.Empty, source, 4, 4, 1, 1, false, 4);
        OnnxBlockwiseQuantizer.QuantizeAndTranspose(
            destination, half, Span<byte>.Empty, source, 4, 4, 1, 1, true, 4);

        //Assert
        single[0].Should().NotBe(half[0]);
        half[0].Should().Be((float)(Half)single[0]);
    }
}
