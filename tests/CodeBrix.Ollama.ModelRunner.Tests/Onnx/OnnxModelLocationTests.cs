using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class OnnxModelLocationTests
{
    [Fact]
    public void Nested_graph_locations_are_relative_to_the_graph_in_both_loading_forms()
    {
        using var scratch = new TempScratchDirectory();
        OnnxModelLocation directory = OnnxModelLocation.ForDirectory(scratch.DirectoryPath, "graphs/model.onnx");
        Assert.Equal(scratch.Combine(Path.Combine("graphs", "weights.bin")), directory.Resolve("weights.bin"));
        Assert.Equal(scratch.Combine(Path.Combine("shared", "weights.bin")), directory.Resolve("../shared/weights.bin"));
        var files = new Dictionary<string, string>
        {
            ["graphs/model.onnx"] = scratch.Combine("graph-blob"),
            ["graphs/weights.bin"] = scratch.Combine("weight-blob"),
            ["shared/weights.bin"] = scratch.Combine("shared-blob")
        };
        OnnxModelLocation mapped = OnnxModelLocation.ForFiles(files, "graphs/model.onnx");
        Assert.Equal(files["graphs/weights.bin"], mapped.Resolve("weights.bin"));
        Assert.Equal(files["shared/weights.bin"], mapped.Resolve("../shared/weights.bin"));
        Assert.Null(mapped.Resolve("missing.bin"));
    }

    [Theory]
    [InlineData("../../outside.bin")]
    [InlineData("/outside.bin")]
    [InlineData("C:\\outside.bin")]
    public void Bundle_external_paths_cannot_escape_the_bundle(string location)
    {
        OnnxModelLocation model = OnnxModelLocation.ForDirectory(Path.GetTempPath(), "graphs/model.onnx");
        Assert.Throws<ModelLoadException>(() => model.Resolve(location));
    }
}
