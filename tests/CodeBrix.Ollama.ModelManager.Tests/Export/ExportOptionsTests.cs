using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what an export does when the caller says nothing: the automatic route, fp32, no name of its
/// own, nothing replaced and no publisher code run.
/// </summary>
public sealed class ExportOptionsTests
{
    [Fact]
    public void the_defaults_are_the_automatic_route_at_fp32_replacing_nothing()
    {
        //Arrange
        var options = new ExportOptions();

        //Act and assert
        options.Route.Should().Be(ExportRoute.Auto);
        options.Precision.Should().Be("fp32");
        options.OutputName.Should().BeNull();
        options.Overwrite.Should().BeFalse();
        options.AllowRemoteCode.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "fp32")]
    [InlineData("", "fp32")]
    [InlineData("   ", "fp32")]
    [InlineData(" int4 ", "int4")]
    [InlineData("fp16", "fp16")]
    public void Precision_never_reads_as_nothing(string assigned, string expected)
        => new ExportOptions { Precision = assigned }.Precision.Should().Be(expected);

    [Fact]
    public void DefaultPrecision_is_what_an_unset_precision_reads_as()
        => ExportOptions.DefaultPrecision.Should().Be(new ExportOptions().Precision);
}
