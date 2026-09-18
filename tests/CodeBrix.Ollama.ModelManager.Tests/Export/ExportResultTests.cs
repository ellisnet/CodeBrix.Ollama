using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what an export hands back: the name to go on using, the files, and which tool at which
/// version produced them.
/// </summary>
public sealed class ExportResultTests
{
    [Fact]
    public void the_result_carries_the_name_the_files_and_the_tool()
    {
        //Arrange
        var result = new ExportResult(
            "hf.co/m-a-p/MuPT-v1-8192-190M:onnx",
            new[] { "genai_config.json", "model.onnx" },
            "onnxruntime-genai",
            "0.15.2",
            ExportRoute.GenAiBuilder);

        //Act and assert
        result.Name.Should().Be("hf.co/m-a-p/MuPT-v1-8192-190M:onnx");
        result.Files.Should().Equal("genai_config.json", "model.onnx");
        result.Tool.Should().Be("onnxruntime-genai");
        result.ToolVersion.Should().Be("0.15.2");
        result.RouteUsed.Should().Be(ExportRoute.GenAiBuilder);
    }

    [Fact]
    public void files_are_never_null()
        => new ExportResult("a:onnx", null, "publisher", null, ExportRoute.PublisherOnnx).Files.Should().BeEmpty();

    [Fact]
    public void ToString_names_the_bundle_the_route_and_how_many_files()
        => new ExportResult("a:onnx", new[] { "model.onnx" }, "publisher", null, ExportRoute.PublisherOnnx)
            .ToString().Should().Be("a:onnx (PublisherOnnx, 1 files)");
}
