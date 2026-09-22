using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>Exercises public exclusion settings through the actual Python quantizers, without downloads.</summary>
public sealed class ReduceNodeExclusionsTests
{
    private readonly PythonTestFixture _fixture;
    public ReduceNodeExclusionsTests(PythonTestFixture fixture) => _fixture = fixture;

    [EnvGatedFact(TestGates.RunPythonTests)]
    public async Task Python_engines_preserve_excluded_nodes_and_reduce_the_other_matrix()
    {
        using var directory = new TempExportDirectory();
        string source = Path.Combine(directory.DirectoryPath, "source");
        Directory.CreateDirectory(source);
        var graph = new OnnxGraphProto { Name = "exclusions" };
        graph.Inputs.Add(Value("x", 64));
        graph.Outputs.Add(Value("y", 8));
        foreach (var row in new[] { (Name: "keep", Input: "x", Output: "middle", Columns: 64),
            (Name: "reduce", Input: "middle", Output: "y", Columns: 8) })
        {
            var tensor = new OnnxTensorProto { Name = row.Name + "_weight", DataType = 1 };
            tensor.Dimensions.Add(64); tensor.Dimensions.Add(row.Columns);
            var floats = Enumerable.Range(0, 64 * row.Columns).Select(i => (float)Math.Sin(i)).ToArray();
            tensor.RawData = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, tensor.RawData, 0, tensor.RawData.Length);
            graph.Initializers.Add(tensor);
            var node = new OnnxNodeProto { Name = row.Name, OpType = "MatMul" };
            node.Inputs.Add(row.Input); node.Inputs.Add(tensor.Name); node.Outputs.Add(row.Output);
            graph.Nodes.Add(node);
        }
        var proto = new OnnxModelProto { IrVersion = 9, Graph = graph };
        proto.OpsetImports.Add(new OnnxOperatorSetId { Version = 17 });
        await File.WriteAllBytesAsync(Path.Combine(source, "model.onnx"), OnnxModel.FromProto(proto, null).Serialize(), TestContext.Current.CancellationToken);
        using var store = new ModelStore(new ModelStoreOptions
        {
            StoreDirectory = Path.Combine(directory.DirectoryPath, "store"), Python = _fixture.Options
        });
        await store.ImportBundleAsync("local/exclusions:source", source, cancellationToken: TestContext.Current.CancellationToken);
        foreach (ReduceMode mode in new[] { ReduceMode.WeightOnlyInt4, ReduceMode.WeightOnlyInt8, ReduceMode.DynamicInt8 })
        {
            ReduceResult result = await store.ReduceOnnxAsync("local/exclusions:source", new ReduceOptions
            {
                Engine = ReduceEngine.Python, Mode = mode, Preprocess = false,
                BlockSize = 32, NodesToExclude = new[] { "keep" }
            }, cancellationToken: TestContext.Current.CancellationToken);
            ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
            OnnxModel reduced = await OnnxModel.ReadAsync(resolved.Files.Single(f => f.Name == "model.onnx").BlobPath, TestContext.Current.CancellationToken);
            Assert.Contains(reduced.Graph.Nodes, n => n.Name == "keep" && n.OpType == "MatMul");
            Assert.Contains(reduced.Graph.Nodes, n => n.OpType == (mode == ReduceMode.DynamicInt8 ? "MatMulInteger" : "MatMulNBits"));
        }
    }

    private static OnnxValueInfoProto Value(string name, int columns)
    {
        var shape = new OnnxTensorShapeProto();
        shape.Dimensions.Add(new OnnxTensorShapeDimension { DimensionValue = 1 });
        shape.Dimensions.Add(new OnnxTensorShapeDimension { DimensionValue = columns });
        return new OnnxValueInfoProto
        {
            Name = name,
            Type = new OnnxTypeProto { TensorType = new OnnxTensorTypeProto { ElementType = 1, Shape = shape } }
        };
    }
}
