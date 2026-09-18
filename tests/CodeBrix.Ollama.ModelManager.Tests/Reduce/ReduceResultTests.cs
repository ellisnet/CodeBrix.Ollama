using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what a reduction hands back: every member it was built with, and the one line it describes
/// itself in.
/// </summary>
public sealed class ReduceResultTests
{
    [Fact]
    public void the_result_reports_everything_it_was_built_with()
    {
        //Arrange
        var files = new[] { "model.onnx", "config.json" };

        //Act
        var result = new ReduceResult(
            "local/x/y:int4-weights", files, ReduceEngine.Python, ReduceMode.WeightOnlyInt4,
            800L, 100L, "onnxruntime", "1.30.0");

        //Assert
        result.Name.Should().Be("local/x/y:int4-weights");
        result.Files.Should().Equal("model.onnx", "config.json");
        result.EngineUsed.Should().Be(ReduceEngine.Python);
        result.Mode.Should().Be(ReduceMode.WeightOnlyInt4);
        result.SourceBytes.Should().Be(800L);
        result.ReducedBytes.Should().Be(100L);
        result.Tool.Should().Be("onnxruntime");
        result.ToolVersion.Should().Be("1.30.0");
    }

    [Fact]
    public void the_result_with_no_files_reports_an_empty_list_rather_than_nothing()
        => new ReduceResult("local/x/y:int8", null, ReduceEngine.Python, ReduceMode.DynamicInt8,
            0L, 0L, "onnxruntime", null).Files.Should().BeEmpty();

    [Fact]
    public void ToString_says_what_was_done_and_how_much_smaller_it_became()
        => new ReduceResult("local/x/y:int4-weights", null, ReduceEngine.Python,
            ReduceMode.WeightOnlyInt4, 800L, 100L, "onnxruntime", "1.30.0")
            .ToString().Should().Be("local/x/y:int4-weights (WeightOnlyInt4, Python, 8.00x smaller)");

    [Fact]
    public void ToString_leaves_the_ratio_out_when_nothing_was_measured()
        => new ReduceResult("local/x/y:onnx-preprocessed", null, ReduceEngine.Python,
            ReduceMode.PreprocessOnly, 0L, 0L, "onnxruntime", "1.30.0")
            .ToString().Should().Be("local/x/y:onnx-preprocessed (PreprocessOnly, Python)");
}
