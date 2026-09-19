using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the arithmetic a conversion does to every weight: widening a checkpoint's numbers, narrowing them to
/// the type being written, and the rotary permutation that shuffles the rows of the query and key projections.
/// </summary>
public sealed class TensorDataConverterTests
{
    [Theory]
    [InlineData(1.0f, 0x3f80)]
    [InlineData(-1.0f, 0xbf80)]
    [InlineData(0.0f, 0x0000)]
    [InlineData(float.PositiveInfinity, 0x7f80)]
    public void ToBFloat16_keeps_the_top_sixteen_bits(float value, int expected)
        => TensorDataConverter.ToBFloat16(value).Should().Be((ushort)expected);

    [Fact]
    public void ToBFloat16_rounds_to_nearest_with_ties_to_even()
    {
        //Arrange - three values whose low sixteen bits are exactly half way, one either side of it.
        float halfWayDown = BitConverter.UInt32BitsToSingle(0x3f808000);
        float halfWayUp = BitConverter.UInt32BitsToSingle(0x3f818000);
        float justOver = BitConverter.UInt32BitsToSingle(0x3f808001);

        //Act and assert
        TensorDataConverter.ToBFloat16(halfWayDown).Should().Be((ushort)0x3f80);
        TensorDataConverter.ToBFloat16(halfWayUp).Should().Be((ushort)0x3f82);
        TensorDataConverter.ToBFloat16(justOver).Should().Be((ushort)0x3f81);
    }

    [Fact]
    public void ToBFloat16_makes_a_signalling_not_a_number_quiet()
    {
        //Arrange
        float signalling = BitConverter.UInt32BitsToSingle(0x7f800001);

        //Act
        ushort result = TensorDataConverter.ToBFloat16(signalling);

        //Assert
        result.Should().Be((ushort)0x7fc0);
    }

    [Fact]
    public async Task ConvertAsync_round_trips_bfloat16_through_bfloat16_unchanged()
    {
        //Arrange
        byte[] source = { 0x80, 0x3f, 0x00, 0xc0, 0x40, 0x3e, 0xff, 0x7f };

        //Act
        byte[] written = await ConvertAsync(source, CheckpointDataType.BF16, GgufTensorType.BF16, 4);

        //Assert
        written.Should().BeEquivalentTo(source);
    }

    [Fact]
    public async Task ConvertAsync_widens_bfloat16_to_float32_exactly()
    {
        //Arrange - 1.0 and -2.0 as bfloat16.
        byte[] source = { 0x80, 0x3f, 0x00, 0xc0 };

        //Act
        byte[] written = await ConvertAsync(source, CheckpointDataType.BF16, GgufTensorType.F32, 2);

        //Assert
        BinaryPrimitives.ReadSingleLittleEndian(written).Should().Be(1.0f);
        BinaryPrimitives.ReadSingleLittleEndian(new ReadOnlySpan<byte>(written, 4, 4)).Should().Be(-2.0f);
    }

    [Fact]
    public async Task ConvertAsync_narrows_a_value_too_large_for_float16_to_infinity()
    {
        //Arrange - 1e30 and 3e-8 as float32: one overflows float16, the other lands on its smallest subnormal.
        var source = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(source, 1.0e30f);
        BinaryPrimitives.WriteSingleLittleEndian(new Span<byte>(source, 4, 4), 3.0e-8f);

        //Act
        byte[] written = await ConvertAsync(source, CheckpointDataType.F32, GgufTensorType.F16, 2);

        //Assert
        ((float)BinaryPrimitives.ReadHalfLittleEndian(written)).Should().Be(float.PositiveInfinity);
        ((float)BinaryPrimitives.ReadHalfLittleEndian(new ReadOnlySpan<byte>(written, 2, 2)))
            .Should().Be((float)Half.Epsilon);
    }

    [Fact]
    public async Task ConvertAsync_permutes_the_rows_the_way_the_engine_does()
    {
        //Arrange - eight rows of one value, two heads, so each head's four rows interleave in pairs.
        var source = new byte[8 * 4];
        for (int i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(new Span<byte>(source, i * 4, 4), i);
        }

        //Act
        byte[] written = await ConvertAsync(source, CheckpointDataType.F32, GgufTensorType.F32, 8,
            permuteHeadCount: 2, rowCount: 8);

        //Assert
        var values = new float[8];
        for (int i = 0; i < 8; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(new ReadOnlySpan<byte>(written, i * 4, 4));
        }

        values.Should().BeEquivalentTo(new float[] { 0, 2, 1, 3, 4, 6, 5, 7 });
    }

    [Fact]
    public async Task ConvertAsync_refuses_a_shape_the_permutation_cannot_divide()
    {
        //Arrange
        var source = new byte[6 * 4];

        //Act
        Func<Task> act = async () => await ConvertAsync(source, CheckpointDataType.F32, GgufTensorType.F32, 6,
            permuteHeadCount: 4, rowCount: 6);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("two halves per head");
    }

    private static async Task<byte[]> ConvertAsync(byte[] source, CheckpointDataType sourceType,
        GgufTensorType targetType, long elementCount, long permuteHeadCount = 0, long rowCount = 1)
    {
        using var input = new MemoryStream(source);
        using var output = new MemoryStream();
        await TensorDataConverter.ConvertAsync(input, output, sourceType, targetType, elementCount,
            permuteHeadCount, rowCount, TestContext.Current.CancellationToken);
        return output.ToArray();
    }
}
