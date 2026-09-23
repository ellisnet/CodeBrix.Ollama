using CodeBrix.Ollama.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable xUnit1051

namespace CodeBrix.Ollama.ModelManager.Tests;

public sealed class OnnxExternalFilesTests
{
    [Theory]
    [InlineData(ReduceMode.WeightOnlyInt4)]
    [InlineData(ReduceMode.WeightOnlyInt8)]
    public async Task Partial_reduction_preserves_shared_arbitrarily_named_weights(ReduceMode mode)
    {
        using var temp = new TempStoreDirectory();
        string source = Path.Combine(temp.DirectoryPath, "source");
        Directory.CreateDirectory(source);
        OnnxModel original = await OnnxModel.ReadAsync(OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"));
        await original.WriteAsync(Path.Combine(source, "a.onnx"),
            new OnnxSaveOptions { UseExternalData = true, ExternalDataFileName = "weights.bin", SizeThreshold = 0 });
        File.Copy(Path.Combine(source, "a.onnx"), Path.Combine(source, "b.onnx"));
        await File.WriteAllTextAsync(Path.Combine(source, "pytorch_model.bin"), "obsolete checkpoint");
        byte[] shared = await File.ReadAllBytesAsync(Path.Combine(source, "weights.bin"));
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = Path.Combine(temp.DirectoryPath, "store") });
        await store.ImportBundleAsync("local/external:source", source);
        ExportResult exported = await store.ExportToOnnxAsync("local/external:source");
        Assert.Contains("weights.bin", exported.Files);
        Assert.DoesNotContain("pytorch_model.bin", exported.Files);
        ReduceResult reduced = await store.ReduceOnnxAsync(exported.Name,
            new ReduceOptions { Mode = mode, Files = new[] { "a.onnx" }, BlockSize = 32, Engine = ReduceEngine.Managed });
        string output = Path.Combine(temp.DirectoryPath, "result");
        await store.MaterializeAsync(reduced.Name, output);
        Assert.Equal(shared, await File.ReadAllBytesAsync(Path.Combine(output, "weights.bin")));
        OnnxModel untouched = await OnnxModel.ReadAsync(Path.Combine(output, "b.onnx"));
        await untouched.LoadExternalDataAsync();
        Assert.Contains(untouched.Graph.Nodes, n => n.OpType == "MatMul");
        OnnxModel quantized = await OnnxModel.ReadAsync(Path.Combine(output, "a.onnx"));
        Assert.Contains(quantized.Graph.Nodes, n => n.OpType == "MatMulNBits");
        Assert.Equal(new FileInfo(Path.Combine(source, "a.onnx")).Length + shared.Length, reduced.SourceBytes);
    }

    [Fact]
    public async Task Shared_data_is_counted_once_when_both_graphs_are_selected()
    {
        using var temp = new TempStoreDirectory();
        string source = Path.Combine(temp.DirectoryPath, "source");
        OnnxModel model = await OnnxModel.ReadAsync(OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"));
        await model.WriteAsync(Path.Combine(source, "a.onnx"),
            new OnnxSaveOptions { UseExternalData = true, ExternalDataFileName = "shared.bin", SizeThreshold = 0 });
        File.Copy(Path.Combine(source, "a.onnx"), Path.Combine(source, "b.onnx"));
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = Path.Combine(temp.DirectoryPath, "store") });
        await store.ImportBundleAsync("local/shared:source", source);
        ReduceResult reduced = await store.ReduceOnnxAsync("local/shared:source", new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4 });
        Assert.Equal(Directory.GetFiles(source).Sum(p => new FileInfo(p).Length), reduced.SourceBytes);
        Assert.DoesNotContain("shared.bin", reduced.Files);
    }

    [Fact]
    public async Task Nested_graph_can_reference_a_shared_file_inside_the_bundle()
    {
        using var temp = new TempStoreDirectory();
        string graph = Path.Combine(temp.DirectoryPath, "nested", "model.onnx");
        OnnxModel model = await OnnxModel.ReadAsync(OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"));
        await model.WriteAsync(graph, new OnnxSaveOptions { UseExternalData = true, SizeThreshold = 0 });
        File.Move(graph + ".data", Path.Combine(temp.DirectoryPath, "weights.bin"));
        foreach (OnnxTensorProto tensor in model.EnumerateTensors())
        {
            foreach (OnnxStringStringEntry entry in tensor.ExternalData)
            {
                if (entry.Key == "location") entry.Value = "../weights.bin";
            }
        }
        await File.WriteAllBytesAsync(graph, model.Serialize());
        Assert.Equal(new[] { Path.Combine(temp.DirectoryPath, "weights.bin") },
            await OnnxExternalFiles.PathsAsync(graph, TestContext.Current.CancellationToken, temp.DirectoryPath));
        await Assert.ThrowsAsync<InvalidDataException>(() => OnnxExternalFiles.PathsAsync(graph, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_new_weight_file_cannot_replace_weights_used_by_an_unselected_graph()
    {
        using var temp = new TempStoreDirectory();
        string graph = Path.Combine(temp.DirectoryPath, "model.onnx");
        OnnxModel model = await OnnxModel.ReadAsync(OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"));
        await model.WriteAsync(graph, new OnnxSaveOptions { UseExternalData = true, SizeThreshold = 0 });
        byte[] expected = await File.ReadAllBytesAsync(graph + ".data");
        Assert.True(await OnnxExternalFiles.AvoidCollisionsAsync(graph, temp.DirectoryPath,
            new HashSet<string> { "model.onnx.data" }, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(graph + ".data"));
        Assert.Equal(expected, await File.ReadAllBytesAsync(graph + ".data.reduced"));
        OnnxModel reloaded = await OnnxModel.ReadAsync(graph);
        await reloaded.LoadExternalDataAsync();
        Assert.Equal(expected, reloaded.Graph.Initializers.Single().RawData);
    }

    [Theory]
    [InlineData(ReduceMode.WeightOnlyInt4)]
    [InlineData(ReduceMode.WeightOnlyInt8)]
    [InlineData(ReduceMode.DynamicInt8)]
    public async Task Public_node_exclusions_preserve_the_named_weight_and_are_recorded(ReduceMode mode)
    {
        using var temp = new TempStoreDirectory();
        string source = Path.Combine(temp.DirectoryPath, "source");
        Directory.CreateDirectory(source);
        string graph = Path.Combine(source, "model.onnx");
        File.Copy(OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.inferred.onnx"), graph);
        OnnxModel model = await OnnxModel.ReadAsync(graph);
        string node = model.Graph.Nodes.Single(n => n.OpType == "MatMul").Name;
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = Path.Combine(temp.DirectoryPath, "store") });
        await store.ImportBundleAsync("local/excluded:source", source);
        ReduceResult result = await store.ReduceOnnxAsync("local/excluded:source",
            new ReduceOptions { Mode = mode, Engine = ReduceEngine.Managed, Preprocess = false, NodesToExclude = new[] { node } });
        string output = Path.Combine(temp.DirectoryPath, "output");
        await store.MaterializeAsync(result.Name, output);
        OnnxModel reduced = await OnnxModel.ReadAsync(Path.Combine(output, "model.onnx"));
        Assert.Contains(reduced.Graph.Nodes, n => n.Name == node && n.OpType == "MatMul");
        Assert.Equal(model.Graph.Initializers.Single().RawData, reduced.Graph.Initializers.Single().RawData);
        Assert.Equal(new[] { node }, OnnxReduce.CopyNodeExclusions(new ReduceOptions { NodesToExclude = new[] { node, node } }));
        Assert.Contains(node, OnnxReduce.SettingsFor(mode, ReduceEngine.Managed,
            new ReduceOptions { NodesToExclude = new[] { node } }, new[] { "model.onnx" })["nodesToExclude"]);
    }

    [Fact]
    public async Task Missing_external_weights_are_rejected_before_export()
    {
        using var temp = new TempStoreDirectory();
        string graph = Path.Combine(temp.DirectoryPath, "model.onnx");
        OnnxModel model = await OnnxModel.ReadAsync(OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"));
        await model.WriteAsync(graph, new OnnxSaveOptions { UseExternalData = true, SizeThreshold = 0 });
        await Assert.ThrowsAsync<InvalidDataException>(() => OnnxExternalFiles.ReadBundleAsync(
            new[] { new ResolvedFile("model.onnx", graph, new FileInfo(graph).Length, "unused") }, TestContext.Current.CancellationToken));
    }
}
