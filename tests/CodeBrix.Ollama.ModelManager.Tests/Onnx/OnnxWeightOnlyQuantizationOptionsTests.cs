using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Pins the defaults the managed weight-only settings inherited from ONNX Runtime's
/// <c>DefaultWeightOnlyQuantConfig</c> and <c>MatMulNBitsQuantizer</c>.
/// </summary>
public sealed class OnnxWeightOnlyQuantizationOptionsTests
{
    [Fact]
    public void BlockSize_defaults_to_the_upstream_value() =>
        new OnnxWeightOnlyQuantizationOptions().BlockSize.Should().Be(128);

    [Fact]
    public void Bits_defaults_to_four() =>
        new OnnxWeightOnlyQuantizationOptions().Bits.Should().Be(4);

    [Fact]
    public void IsSymmetric_defaults_to_asymmetric() =>
        new OnnxWeightOnlyQuantizationOptions().IsSymmetric.Should().BeFalse();

    [Fact]
    public void AccuracyLevel_defaults_to_unset() =>
        new OnnxWeightOnlyQuantizationOptions().AccuracyLevel.Should().BeNull();

    [Fact]
    public void OpTypesToQuantize_defaults_to_matmul_alone() =>
        new OnnxWeightOnlyQuantizationOptions().OpTypesToQuantize.Should().BeEquivalentTo(new[] { "MatMul" });

    [Fact]
    public void NodesToExclude_defaults_to_empty() =>
        new OnnxWeightOnlyQuantizationOptions().NodesToExclude.Should().BeEmpty();

    [Fact]
    public void NodesToInclude_defaults_to_empty() =>
        new OnnxWeightOnlyQuantizationOptions().NodesToInclude.Should().BeEmpty();
}
