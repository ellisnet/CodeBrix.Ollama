using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Pins the defaults of the codec's write settings, which follow ONNX's own external-data convention.
/// </summary>
public sealed class OnnxSaveOptionsTests
{
    [Fact]
    public void UseExternalData_defaults_to_one_whole_file() =>
        new OnnxSaveOptions().UseExternalData.Should().BeFalse();

    [Fact]
    public void SizeThreshold_defaults_to_the_value_onnx_uses() =>
        new OnnxSaveOptions().SizeThreshold.Should().Be(1024);

    [Fact]
    public void ExternalDataFileName_defaults_to_unset() =>
        new OnnxSaveOptions().ExternalDataFileName.Should().BeNull();
}
