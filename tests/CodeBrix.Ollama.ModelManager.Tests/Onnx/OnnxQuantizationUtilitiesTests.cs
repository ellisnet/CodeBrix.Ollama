using System;
using CodeBrix.Ollama.Core;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Pins every default the managed engine inherited from ONNX Runtime's <c>quant_utils</c>: the quantized ranges, the
/// types that are symmetric unless told otherwise, the scale and zero-point formula, the element-wise quantization and
/// the four-bit packing.
/// </summary>
public sealed class OnnxQuantizationUtilitiesTests
{
    [Theory]
    [InlineData(2, false, false, 0, 255)]
    [InlineData(3, false, false, -128, 127)]
    [InlineData(3, false, true, -127, 127)]
    [InlineData(2, false, true, 0, 255)]
    [InlineData(2, true, false, 0, 127)]
    [InlineData(3, true, false, -64, 64)]
    [InlineData(3, true, true, -64, 64)]
    public void GetQuantizedRange_pins_the_ranges_the_python_tables_hold(
        int type,
        bool reduceRange,
        bool symmetric,
        int expectedMinimum,
        int expectedMaximum)
    {
        //Act
        OnnxQuantizationUtilities.GetQuantizedRange(
            (OnnxTensorDataType)type, reduceRange, symmetric, out int minimum, out int maximum);

        //Assert
        minimum.Should().Be(expectedMinimum);
        maximum.Should().Be(expectedMaximum);
    }

    [Fact]
    public void GetQuantizedRange_refuses_a_type_the_managed_engine_does_not_cover()
    {
        //Arrange
        Action act = () =>
            OnnxQuantizationUtilities.GetQuantizedRange(OnnxTensorDataType.Int16, false, false, out _, out _);

        //Act and assert
        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(22, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(17, true)]
    [InlineData(2, false)]
    [InlineData(21, false)]
    public void IsWeightSymmetric_pins_the_types_that_are_symmetric_unless_told_otherwise(int type, bool expected) =>
        OnnxQuantizationUtilities.IsWeightSymmetric((OnnxTensorDataType)type).Should().Be(expected);

    [Fact]
    public void ComputeScaleAndZeroPoint_gives_a_symmetric_signed_range_a_zero_point_of_zero()
    {
        //Act
        float scale = OnnxQuantizationUtilities.ComputeScaleAndZeroPoint(
            -1.0f, 2.0f, -127, 127, true, false, out int zeroPoint);

        //Assert
        zeroPoint.Should().Be(0);
        scale.Should().Be(4.0f / 254f);
    }

    [Fact]
    public void ComputeScaleAndZeroPoint_gives_a_symmetric_unsigned_range_a_zero_point_of_one_hundred_and_twenty_eight()
    {
        //Act
        OnnxQuantizationUtilities.ComputeScaleAndZeroPoint(-1.0f, 2.0f, 0, 255, true, false, out int zeroPoint);

        //Assert
        zeroPoint.Should().Be(128);
    }

    [Fact]
    public void ComputeScaleAndZeroPoint_widens_an_asymmetric_range_to_include_zero()
    {
        //Act
        float scale = OnnxQuantizationUtilities.ComputeScaleAndZeroPoint(
            0.5f, 2.5f, 0, 255, false, false, out int zeroPoint);

        //Assert
        scale.Should().Be(2.5f / 255f);
        zeroPoint.Should().Be(0);
    }

    [Fact]
    public void ComputeScaleAndZeroPoint_falls_back_to_a_scale_of_one_when_the_range_is_empty()
    {
        //Act
        float scale = OnnxQuantizationUtilities.ComputeScaleAndZeroPoint(
            0f, 0f, -128, 127, false, false, out int zeroPoint);

        //Assert
        scale.Should().Be(1f);
        zeroPoint.Should().Be(0);
    }

    [Fact]
    public void QuantizeArray_rounds_halves_to_even_and_clamps_to_the_full_range()
    {
        //Act
        int[] quantized = OnnxQuantizationUtilities.QuantizeArray(
            OnnxTensorDataType.Int8,
            new[] { 0.5f, 1.5f, 2.5f, -0.5f, 1000f, -1000f },
            1f,
            0);

        //Assert
        quantized.Should().BeEquivalentTo(new[] { 0, 2, 2, 0, 127, -128 });
    }

    [Fact]
    public void PackBytesTo4Bit_puts_the_first_value_in_the_low_nibble() =>
        OnnxQuantizationUtilities.PackBytesTo4Bit(new byte[] { 1, 2, 3 })
            .Should().BeEquivalentTo(new byte[] { 0x21, 0x03 });

    [Fact]
    public void PackBytesTo4Bit_with_no_values_returns_no_bytes() =>
        OnnxQuantizationUtilities.PackBytesTo4Bit(Array.Empty<byte>()).Should().BeEmpty();

    [Fact]
    public void ProducerName_pins_what_the_python_quantizer_stamps()
    {
        //Act and assert
        OnnxQuantizationUtilities.ProducerName.Should().Be("onnx.quantize");
        OnnxQuantizationUtilities.ProducerVersion.Should().Be("0.1.0");
        OnnxQuantizationUtilities.TensorNameQuantSuffix.Should().Be("_quantized");
        OnnxQuantizationUtilities.MicrosoftDomain.Should().Be("com.microsoft");
        OnnxQuantizationUtilities.InferMetadataKey.Should().Be("onnx.infer");
        OnnxQuantizationUtilities.InferMetadataValue.Should().Be("onnxruntime.quant");
    }

    [Fact]
    public void ReadFloatElements_reads_half_precision_raw_bytes()
    {
        //Arrange
        OnnxTensorProto tensor = OnnxQuantizationUtilities.MakeRawTensor(
            "half",
            OnnxTensorDataType.Float16,
            new long[] { 2 },
            new byte[] { 0x00, 0x3C, 0x00, 0xC0 });

        //Act
        float[] values = OnnxQuantizationUtilities.ReadFloatElements(tensor, tensor.RawData);

        //Assert
        values.Should().BeEquivalentTo(new[] { 1f, -2f });
    }

    [Fact]
    public void ReadFloatElements_refuses_a_type_that_is_not_floating_point()
    {
        //Arrange
        OnnxTensorProto tensor = OnnxQuantizationUtilities.MakeRawTensor(
            "integers",
            OnnxTensorDataType.Int8,
            new long[] { 1 },
            new byte[] { 1 });
        Action act = () => OnnxQuantizationUtilities.ReadFloatElements(tensor, tensor.RawData);

        //Act and assert
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void MakeFloatTensor_stores_half_precision_values_in_the_integer_field()
    {
        //Act
        OnnxTensorProto tensor = OnnxQuantizationUtilities.MakeFloatTensor(
            "scale",
            OnnxTensorDataType.Float16,
            Array.Empty<long>(),
            new[] { 1f });

        //Assert
        tensor.Int32Data.Should().BeEquivalentTo(new[] { 0x3C00 });
        tensor.FloatData.Should().BeEmpty();
        tensor.RawData.Should().BeNull();
    }
}
