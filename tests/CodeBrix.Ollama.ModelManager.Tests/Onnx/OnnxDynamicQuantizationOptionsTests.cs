using CodeBrix.Ollama.Core;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Pins the defaults the managed dynamic settings inherited from ONNX Runtime's <c>quantize_dynamic</c>.
/// </summary>
public sealed class OnnxDynamicQuantizationOptionsTests
{
    [Fact]
    public void WeightType_defaults_to_signed_eight_bit() =>
        new OnnxDynamicQuantizationOptions().WeightType.Should().Be(OnnxTensorDataType.Int8);

    [Fact]
    public void PerChannel_defaults_to_one_scale_for_the_whole_weight() =>
        new OnnxDynamicQuantizationOptions().PerChannel.Should().BeFalse();

    [Fact]
    public void ReduceRange_defaults_to_the_full_range() =>
        new OnnxDynamicQuantizationOptions().ReduceRange.Should().BeFalse();

    [Fact]
    public void NodesToExclude_defaults_to_empty() =>
        new OnnxDynamicQuantizationOptions().NodesToExclude.Should().BeEmpty();

    [Fact]
    public void NodesToQuantize_defaults_to_empty() =>
        new OnnxDynamicQuantizationOptions().NodesToQuantize.Should().BeEmpty();
}
