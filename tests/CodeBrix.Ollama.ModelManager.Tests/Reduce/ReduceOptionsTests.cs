using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what a reduction does when the caller says nothing, which matters more here than almost
/// anywhere else in this library: the defaults ARE ONNX Runtime's own defaults, and the tests that pin
/// them read the managed engine's option classes rather than repeating the numbers, so the two engines
/// cannot drift apart without a test saying so.
/// </summary>
public sealed class ReduceOptionsTests
{
    [Fact]
    public void Mode_defaults_to_dynamic_eight_bit()
        => new ReduceOptions().Mode.Should().Be(ReduceMode.DynamicInt8);

    [Fact]
    public void Engine_defaults_to_choosing_one()
        => new ReduceOptions().Engine.Should().Be(ReduceEngine.Auto);

    [Fact]
    public void Preprocess_defaults_to_preparing_the_graph()
        => new ReduceOptions().Preprocess.Should().BeTrue();

    [Fact]
    public void Files_defaults_to_every_graph_in_the_bundle()
        => new ReduceOptions().Files.Should().BeNull();

    [Fact]
    public void OutputName_defaults_to_the_name_the_mode_decides()
        => new ReduceOptions().OutputName.Should().BeNull();

    [Fact]
    public void Overwrite_defaults_to_refusing_to_replace_anything()
        => new ReduceOptions().Overwrite.Should().BeFalse();

    [Fact]
    public void the_block_wise_defaults_are_the_ones_the_managed_engine_was_ported_with()
    {
        //Arrange
        var options = new ReduceOptions();
        var pinned = new OnnxWeightOnlyQuantizationOptions();

        //Act and assert
        options.BlockSize.Should().Be(pinned.BlockSize);
        options.IsSymmetric.Should().Be(pinned.IsSymmetric);
        options.AccuracyLevel.Should().Be(pinned.AccuracyLevel);
        ReduceOptions.DefaultBlockSize.Should().Be(pinned.BlockSize);
    }

    [Fact]
    public void the_weight_only_modes_ask_for_the_bit_counts_the_managed_engine_knows()
    {
        //Arrange
        var pinned = new OnnxWeightOnlyQuantizationOptions();

        //Act and assert
        OnnxReduce.BitsFor(ReduceMode.WeightOnlyInt4).Should().Be(pinned.Bits);
        OnnxReduce.BitsFor(ReduceMode.WeightOnlyInt8).Should().Be(8);
    }

    [Fact]
    public void the_dynamic_mode_quantizes_to_the_weight_type_the_managed_engine_defaults_to()
        => new OnnxDynamicQuantizationOptions().WeightType.Should().Be(OnnxTensorDataType.Int8);

    [Fact]
    public void the_dynamic_mode_matches_the_managed_engines_channel_and_range_defaults()
    {
        //Arrange
        var pinned = new OnnxDynamicQuantizationOptions();

        //Act and assert
        pinned.PerChannel.Should().BeFalse();
        pinned.ReduceRange.Should().BeFalse();
    }

    [Fact]
    public void BlockSize_keeps_what_it_is_given()
        => new ReduceOptions { BlockSize = 32 }.BlockSize.Should().Be(32);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-128)]
    public void BlockSize_refuses_a_value_that_is_not_a_size(int blockSize)
    {
        //Arrange
        Action act = () => _ = new ReduceOptions { BlockSize = blockSize };

        //Act and assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AccuracyLevel_keeps_what_it_is_given()
        => new ReduceOptions { AccuracyLevel = 4 }.AccuracyLevel.Should().Be(4);
}
