using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the ported graph surgery on its own: the Gemm rewrite and its conditions, dropping initializers nothing
/// asks for, setting an operator set, and the topological sort that decides the node order of the written file.
/// </summary>
public sealed class OnnxGraphEditorTests
{
    [Fact]
    public async Task ReplaceGemmWithMatMul_transposes_an_initializer_b_side_in_place()
    {
        //Arrange
        OnnxModel model = await LoadAsync("gemm_bias_transb_fp32.onnx");

        //Act
        OnnxGraphEditor.ReplaceGemmWithMatMul(model.Graph);
        OnnxTensorProto weight = model.Graph.FindInitializer("weight");

        //Assert
        weight.Dimensions.Should().BeEquivalentTo(new long[] { 48, 6 });
        model.Graph.Nodes.Select(node => node.OpType).Should().BeEquivalentTo(new[] { "MatMul", "Add" });
        model.Graph.Nodes[0].Name.Should().Be("gemm_MatMul");
        model.Graph.Nodes[0].Outputs.Should().BeEquivalentTo(new[] { "output_MatMul" });
        model.Graph.Nodes[1].Name.Should().Be("gemm_Add");
        model.Graph.Nodes[1].Inputs.Should().BeEquivalentTo(new[] { "output_MatMul", "bias" });
    }

    [Fact]
    public async Task ReplaceGemmWithMatMul_leaves_the_output_name_alone_when_there_is_no_bias()
    {
        //Arrange
        OnnxModel model = await LoadAsync("gemm_plain_fp32.onnx");

        //Act
        OnnxGraphEditor.ReplaceGemmWithMatMul(model.Graph);

        //Assert
        model.Graph.Nodes.Should().HaveCount(1);
        model.Graph.Nodes[0].OpType.Should().Be("MatMul");
        model.Graph.Nodes[0].Outputs.Should().BeEquivalentTo(new[] { "output" });
    }

    [Fact]
    public async Task ReplaceGemmWithMatMul_leaves_a_gemm_with_a_scaling_alpha_alone()
    {
        //Arrange
        OnnxModel model = await LoadAsync("gemm_plain_fp32.onnx");
        model.Graph.Nodes[0].Attributes.Add(new OnnxAttributeProto
        {
            Name = "alpha",
            Float = 2.0f,
            AttributeType = (int)OnnxAttributeType.Float,
        });

        //Act
        OnnxGraphEditor.ReplaceGemmWithMatMul(model.Graph);

        //Assert
        model.Graph.Nodes[0].OpType.Should().Be("Gemm");
    }

    [Fact]
    public async Task CleanInitializers_drops_what_nothing_asks_for()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");
        model.Graph.Initializers.Add(OnnxQuantizationUtilities.MakeRawTensor(
            "orphan",
            OnnxTensorDataType.Float,
            new long[] { 1 },
            new byte[] { 0, 0, 0, 0 }));

        //Act
        HashSet<string> missing = OnnxGraphEditor.CleanInitializers(model.Graph);

        //Assert
        missing.Should().BeEmpty();
        model.Graph.Initializers.Select(initializer => initializer.Name).Should().BeEquivalentTo(new[] { "weight" });
    }

    [Fact]
    public async Task SetOpsetImport_adds_a_domain_the_model_does_not_carry()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act
        OnnxGraphEditor.SetOpsetImport(model.Proto, "com.microsoft", 1);
        OnnxGraphEditor.SetOpsetImport(model.Proto, string.Empty, 21);

        //Assert
        model.Proto.OpsetImports.Should().HaveCount(2);
        model.Proto.OpsetImports[0].Version.Should().Be(21L);
        model.Proto.OpsetImports[1].Domain.Should().Be("com.microsoft");
    }

    [Fact]
    public async Task GetOpsetVersion_reads_the_default_domain()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act and assert
        OnnxGraphEditor.GetOpsetVersion(model.Proto).Should().Be(17L);
    }

    [Fact]
    public async Task TopologicalSort_puts_every_node_after_what_feeds_it()
    {
        //Arrange
        OnnxModel model = await LoadAsync("two_matmuls_fp32.onnx");
        model.Graph.Nodes.Reverse();

        //Act
        OnnxGraphEditor.TopologicalSort(model.Graph);

        //Assert
        model.Graph.Nodes.Select(node => node.Name).Should().BeEquivalentTo(new[] { "matmul_one", "matmul_two" });
    }

    [Fact]
    public async Task TopologicalSort_refuses_a_graph_that_is_not_acyclic()
    {
        //Arrange
        OnnxModel model = await LoadAsync("two_matmuls_fp32.onnx");
        model.Graph.Nodes[0].Inputs[0] = "output";
        Action act = () => OnnxGraphEditor.TopologicalSort(model.Graph);

        //Act and assert
        act.Should().Throw<System.IO.InvalidDataException>();
    }

    [Fact]
    public async Task GetNonInitializerInputs_lists_only_the_inputs_the_caller_must_supply()
    {
        //Arrange
        OnnxModel model = await LoadAsync("non_constant_b_fp32.onnx");

        //Act and assert
        OnnxGraphEditor.GetNonInitializerInputs(model.Graph).Should().BeEquivalentTo(new[] { "input", "other" });
    }

    private static async Task<OnnxModel> LoadAsync(string name) =>
        await OnnxModel.ReadAsync(OnnxFixtureFiles.FullPath(name), TestContext.Current.CancellationToken);
}
