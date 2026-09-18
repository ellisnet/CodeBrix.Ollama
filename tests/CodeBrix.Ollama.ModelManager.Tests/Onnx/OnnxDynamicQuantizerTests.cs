using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Compares the managed dynamic quantizer with the checked-in oracle: what ONNX Runtime's <c>quantize_dynamic</c>
/// produced for MatMul and for the Gemm it first rewrites into a MatMul, in every weight type, per-channel and
/// reduced-range combination the managed engine covers.
/// </summary>
public sealed class OnnxDynamicQuantizerTests
{
    [Fact]
    public async Task Process_matches_the_oracle_for_every_fixture_and_mode()
    {
        //Arrange
        List<string> failures = new List<string>();
        int compared = 0;

        //Act
        foreach (string input in OnnxFixtureFiles.DynamicInputs())
        {
            foreach (OnnxDynamicFixtureMode mode in OnnxFixtureFiles.DynamicModes())
            {
                string expectedName = $"{input}.{mode.Suffix}.onnx";
                if (!OnnxFixtureFiles.Exists(expectedName))
                {
                    continue;
                }

                compared++;
                IReadOnlyList<string> differences = await CompareAsync($"{input}.inferred.onnx", expectedName, mode);
                if (differences.Count > 0)
                {
                    failures.Add($"{expectedName}: {differences[0]}");
                }
            }
        }

        //Assert
        failures.Should().BeEmpty();
        compared.Should().Be(42);
    }

    [Fact]
    public async Task Process_leaves_an_already_quantized_model_alone_apart_from_the_value_info_inference_adds()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.dyn_i8.onnx");
        OnnxModel expected = await LoadAsync("matmul_k64_n8_fp32.dyn_i8.dyn_i8.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));
        model.Graph.ValueInfos.Clear();
        expected.Graph.ValueInfos.Clear();

        //Assert
        OnnxModelComparison.Compare(expected.Proto, model.Proto).Should().BeEmpty();
    }

    [Fact]
    public async Task Process_adds_no_value_info_of_its_own_because_it_runs_no_shape_inference()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.dyn_i8.onnx");
        OnnxModel expected = await LoadAsync("matmul_k64_n8_fp32.dyn_i8.dyn_i8.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));

        //Assert
        model.Graph.ValueInfos.Should().BeEmpty();
        expected.Graph.ValueInfos.Should().HaveCount(6);
    }

    [Fact]
    public async Task Process_output_survives_a_codec_round_trip()
    {
        //Arrange
        OnnxModel model = await LoadAsync("gemm_bias_transb_fp32.inferred.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));
        byte[] first = model.Serialize();
        byte[] second = OnnxModel.Parse(first, null).Serialize();

        //Assert
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task Process_rewrites_a_gemm_with_a_bias_into_a_matmul_and_an_add()
    {
        //Arrange
        OnnxModel model = await LoadAsync("gemm_bias_transb_fp32.inferred.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));

        //Assert
        model.Graph.Nodes.Select(node => node.OpType).Should().BeEquivalentTo(
            new[] { "DynamicQuantizeLinear", "Mul", "MatMulInteger", "Cast", "Mul", "Add" });
        model.Graph.FindInitializer("weight_quantized").Dimensions.Should().BeEquivalentTo(new long[] { 48, 6 });
    }

    [Fact]
    public async Task Process_leaves_a_matmul_whose_b_side_is_not_constant_alone()
    {
        //Arrange
        OnnxModel model = await LoadAsync("non_constant_b_fp32.inferred.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));

        //Assert
        model.Graph.Nodes.Should().HaveCount(1);
        model.Graph.Nodes[0].OpType.Should().Be("MatMul");
    }

    [Fact]
    public async Task Process_gives_a_signed_weight_a_zero_point_of_zero()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.inferred.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));
        OnnxTensorProto zeroPoint = model.Graph.FindInitializer("weight_zero_point");
        OnnxTensorProto weight = model.Graph.FindInitializer("weight_quantized");

        //Assert
        zeroPoint.DataType.Should().Be((int)OnnxTensorDataType.Int8);
        zeroPoint.Int32Data.Should().BeEquivalentTo(new[] { 0 });
        zeroPoint.Dimensions.Should().BeEmpty();
        weight.DataType.Should().Be((int)OnnxTensorDataType.Int8);
        weight.RawData.Should().HaveCount(64 * 8);
    }

    [Fact]
    public async Task Process_gives_an_unsigned_weight_one_scale_for_each_output_channel_when_asked()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.inferred.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.UInt8, true, false)));
        OnnxTensorProto scale = model.Graph.FindInitializer("weight_scale");
        OnnxTensorProto zeroPoint = model.Graph.FindInitializer("weight_zero_point");

        //Assert
        scale.Dimensions.Should().BeEquivalentTo(new long[] { 8 });
        scale.FloatData.Should().HaveCount(8);
        zeroPoint.Dimensions.Should().BeEquivalentTo(new long[] { 8 });
        zeroPoint.DataType.Should().Be((int)OnnxTensorDataType.UInt8);
    }

    [Fact]
    public async Task Process_stamps_the_producer_the_python_tools_stamp()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.inferred.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));

        //Assert
        model.Proto.ProducerName.Should().Be("onnx.quantize");
        model.Proto.ProducerVersion.Should().Be("0.1.0");
    }

    [Theory]
    //An embedding table read by a Gather and a Transpose after the MatMul: both are operators ONNX Runtime's integer
    //registry rewrites when it is asked for its whole default set, and this library asks for two operator types only,
    //so both have to come through untouched. The table is also large enough to carry the explicit default data
    //location that the tools' own save-and-reload leaves behind.
    [InlineData("gather_transpose_fp32.inferred.onnx", "gather_transpose_fp32.dyn_i8_mm.onnx")]
    //A branch, so that the node order the sort produces is part of what is compared.
    [InlineData("branch_matmuls_fp32.inferred.onnx", "branch_matmuls_fp32.dyn_i8_mm.onnx")]
    public async Task Process_matches_the_oracle_for_a_graph_shaped_like_a_real_one(
        string inputName, string expectedName)
    {
        //Act
        IReadOnlyList<string> differences = await CompareAsync(
            inputName, expectedName, new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false));

        //Assert
        differences.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_marks_a_surviving_weight_the_way_a_trip_through_a_side_file_does()
    {
        //Arrange
        OnnxModel model = await LoadAsync("gather_transpose_fp32.inferred.onnx");

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));

        //Assert
        model.Graph.FindInitializer("table").DataLocation.Should().Be(0);
    }

    [Fact]
    public async Task Process_refuses_a_model_that_has_not_been_through_shape_inference()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act
        Action act = () => OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));

        //Assert
        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("onnx.infer");
    }

    [Fact]
    public async Task Process_refuses_a_weight_type_outside_the_managed_scope()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.inferred.onnx");

        //Act
        Action act = () => OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.UInt4, false, false)));

        //Assert
        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("8-bit");
    }

    [Fact]
    public async Task Process_refuses_an_operator_the_integer_registry_would_also_rewrite_when_it_is_asked_for()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.inferred.onnx");
        OnnxNodeProto transpose = new OnnxNodeProto { OpType = "Transpose", Name = "transpose" };
        transpose.Inputs.Add("output");
        transpose.Outputs.Add("transposed");
        model.Graph.Nodes.Add(transpose);
        OnnxDynamicQuantizationOptions options = BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false));
        options.OpTypesToQuantize.Add("Transpose");

        //Act
        Action act = () => OnnxDynamicQuantizer.Process(model, options);

        //Assert
        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("Transpose");
    }

    [Fact]
    public async Task Process_carries_an_operator_nobody_asked_about_through_untouched()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.inferred.onnx");
        OnnxNodeProto transpose = new OnnxNodeProto { OpType = "Transpose", Name = "transpose" };
        transpose.Inputs.Add("output");
        transpose.Outputs.Add("transposed");
        model.Graph.Nodes.Add(transpose);

        //Act
        OnnxDynamicQuantizer.Process(model, BuildOptions(
            new OnnxDynamicFixtureMode("x", OnnxTensorDataType.Int8, false, false)));

        //Assert
        model.Graph.Nodes.Should().ContainSingle(node => node.OpType == "Transpose");
        model.Graph.Nodes.Should().ContainSingle(node => node.OpType == "MatMulInteger");
    }

    [Fact]
    public void DynamicOptions_ask_for_the_two_operator_types_this_library_quantizes()
        => new OnnxDynamicQuantizationOptions().OpTypesToQuantize
            .Should().BeEquivalentTo(new[] { "MatMul", "Gemm" });

    private static async Task<IReadOnlyList<string>> CompareAsync(
        string inputName,
        string expectedName,
        OnnxDynamicFixtureMode mode)
    {
        OnnxModel model = await LoadAsync(inputName);
        OnnxModel expected = await LoadAsync(expectedName);
        OnnxDynamicQuantizer.Process(model, BuildOptions(mode));
        List<string> differences = new List<string>(OnnxModelComparison.Compare(expected.Proto, model.Proto));
        if (differences.Count == 0 && !model.Serialize().SequenceEqual(
                File.ReadAllBytes(OnnxFixtureFiles.FullPath(expectedName))))
        {
            differences.Add("the encoded bytes differ although every compared field matches");
        }

        return differences;
    }

    private static async Task<OnnxModel> LoadAsync(string name) =>
        await OnnxModel.ReadAsync(OnnxFixtureFiles.FullPath(name), TestContext.Current.CancellationToken);

    private static OnnxDynamicQuantizationOptions BuildOptions(OnnxDynamicFixtureMode mode) =>
        new OnnxDynamicQuantizationOptions
        {
            WeightType = mode.WeightType,
            PerChannel = mode.PerChannel,
            ReduceRange = mode.ReduceRange,
        };
}
