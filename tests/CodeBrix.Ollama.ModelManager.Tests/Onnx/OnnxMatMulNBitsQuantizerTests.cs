using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Compares the managed weight-only quantizer with the checked-in oracle: the graph ONNX Runtime's own Python tools
/// produced from the same input, in every mode the managed engine covers, with every initializer compared byte for
/// byte. Also pins the packed layout's sizes and the cases the managed engine refuses.
/// </summary>
public sealed class OnnxMatMulNBitsQuantizerTests
{
    [Fact]
    public async Task Process_matches_the_oracle_for_every_fixture_and_mode()
    {
        //Arrange
        List<string> failures = new List<string>();
        int compared = 0;

        //Act
        foreach (string input in OnnxFixtureFiles.WeightOnlyInputs())
        {
            foreach (OnnxWeightOnlyFixtureMode mode in OnnxFixtureFiles.WeightOnlyModes())
            {
                string expectedName = $"{input}.{mode.Suffix}.onnx";
                if (!OnnxFixtureFiles.Exists(expectedName))
                {
                    continue;
                }

                compared++;
                IReadOnlyList<string> differences = await CompareAsync($"{input}.onnx", expectedName, mode);
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

    [Theory]
    //A branch whose nodes come out of the quantizers' own sort in another order.
    [InlineData("branch_matmuls_fp32.onnx", "branch_matmuls_fp32.nbits_b4_bs32_asym.onnx")]
    //An embedding table read by a Gather, which no weight-only mode touches, and a Transpose after it.
    [InlineData("gather_transpose_fp32.onnx", "gather_transpose_fp32.nbits_b4_bs32_asym.onnx")]
    public async Task Process_matches_the_oracle_for_a_graph_shaped_like_a_real_one(
        string inputName, string expectedName)
    {
        //Act
        IReadOnlyList<string> differences = await CompareAsync(
            inputName, expectedName, new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null));

        //Assert
        differences.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_matches_the_oracle_for_a_model_whose_weights_were_beside_it()
    {
        //Arrange
        OnnxModel model = await LoadAsync("external_gather_fp32.onnx");
        await model.LoadExternalDataAsync(TestContext.Current.CancellationToken);
        OnnxModel expected = await LoadAsync("external_gather_fp32.nbits_b4_bs32_asym.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(
            model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));

        //Assert
        OnnxModelComparison.Compare(expected.Proto, model.Proto).Should().BeEmpty();
        model.Serialize().Should().Equal(
            File.ReadAllBytes(OnnxFixtureFiles.FullPath("external_gather_fp32.nbits_b4_bs32_asym.onnx")));
    }

    [Fact]
    public async Task Process_puts_the_nodes_in_the_order_the_tools_write_them_in()
    {
        //Arrange
        OnnxModel model = await LoadAsync("branch_matmuls_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(
            model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));

        //Assert
        model.Graph.Nodes.Select(node => node.Name).Should().Equal(
            "matmul_first_Q4", "matmul_right_Q4", "matmul_left_Q4", "add");
    }

    [Fact]
    public async Task Process_output_survives_a_codec_round_trip()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k70_n5_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));
        byte[] first = model.Serialize();
        byte[] second = OnnxModel.Parse(first, null).Serialize();

        //Assert
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task Process_leaves_an_already_quantized_model_alone()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.nbits_b4_bs32_asym.onnx");
        OnnxModel expected = await LoadAsync("matmul_k64_n8_fp32.nbits_b4_bs32_asym.nbits_b4_bs32_asym.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));

        //Assert
        OnnxModelComparison.Compare(expected.Proto, model.Proto).Should().BeEmpty();
    }

    [Fact]
    public async Task Process_packs_an_exact_block_count_into_the_size_the_layout_asks_for()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));
        OnnxTensorProto packed = model.Graph.FindInitializer("weight_Q4");
        OnnxTensorProto scales = model.Graph.FindInitializer("weight_scales");
        OnnxTensorProto zeroPoints = model.Graph.FindInitializer("weight_zero_points");

        //Assert
        packed.Dimensions.Should().BeEquivalentTo(new long[] { 8, 2, 16 });
        packed.RawData.Should().HaveCount(2 * 8 * 32 / 2);
        scales.Dimensions.Should().BeEquivalentTo(new long[] { 8, 2 });
        scales.RawData.Should().HaveCount(2 * 8 * 4);
        zeroPoints.Dimensions.Should().BeEquivalentTo(new long[] { 8, 1 });
        zeroPoints.RawData.Should().HaveCount(8);
    }

    [Fact]
    public async Task Process_rounds_a_row_count_that_is_not_a_multiple_of_the_block_size_up()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k70_n5_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));
        OnnxTensorProto packed = model.Graph.FindInitializer("weight_Q4");
        OnnxTensorProto scales = model.Graph.FindInitializer("weight_scales");
        OnnxTensorProto zeroPoints = model.Graph.FindInitializer("weight_zero_points");

        //Assert
        packed.Dimensions.Should().BeEquivalentTo(new long[] { 5, 3, 16 });
        packed.RawData.Should().HaveCount(3 * 5 * 32 / 2);
        scales.Dimensions.Should().BeEquivalentTo(new long[] { 5, 3 });
        zeroPoints.Dimensions.Should().BeEquivalentTo(new long[] { 5, 2 });
    }

    [Fact]
    public async Task Process_packs_eight_bit_values_one_to_a_byte()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 8, 32, false, null)));
        OnnxTensorProto packed = model.Graph.FindInitializer("weight_Q8");
        OnnxTensorProto zeroPoints = model.Graph.FindInitializer("weight_zero_points");

        //Assert
        packed.Dimensions.Should().BeEquivalentTo(new long[] { 8, 2, 32 });
        packed.RawData.Should().HaveCount(2 * 8 * 32);
        zeroPoints.Dimensions.Should().BeEquivalentTo(new long[] { 8, 2 });
    }

    [Fact]
    public async Task Process_leaves_the_zero_points_out_when_quantization_is_symmetric()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, true, null)));

        //Assert
        model.Graph.FindInitializer("weight_zero_points").Should().BeNull();
        model.Graph.Nodes[0].Inputs.Should().HaveCount(3);
    }

    [Fact]
    public async Task Process_adds_the_contrib_operator_set_even_when_it_quantizes_nothing()
    {
        //Arrange
        OnnxModel model = await LoadAsync("gemm_plain_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));

        //Assert
        model.Graph.Nodes[0].OpType.Should().Be("Gemm");
        model.Proto.OpsetImports.Should().HaveCount(2);
        model.Proto.OpsetImports[1].Domain.Should().Be("com.microsoft");
        model.Proto.OpsetImports[1].Version.Should().Be(1L);
    }

    [Fact]
    public async Task Process_writes_the_accuracy_level_only_when_it_is_not_zero()
    {
        //Arrange
        OnnxModel withLevel = await LoadAsync("matmul_k64_n8_fp32.onnx");
        OnnxModel withZero = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act
        OnnxMatMulNBitsQuantizer.Process(withLevel, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, 4)));
        OnnxMatMulNBitsQuantizer.Process(withZero, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, 0)));

        //Assert
        withLevel.Graph.Nodes[0].Attributes.Select(attribute => attribute.Name)
            .Should().BeEquivalentTo(new[] { "K", "N", "accuracy_level", "bits", "block_size" });
        withZero.Graph.Nodes[0].Attributes.Select(attribute => attribute.Name)
            .Should().BeEquivalentTo(new[] { "K", "N", "bits", "block_size" });
    }

    [Fact]
    public async Task Process_refuses_an_operator_type_outside_the_managed_scope()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");
        OnnxWeightOnlyQuantizationOptions options = BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null));
        options.OpTypesToQuantize.Add("Gather");

        //Act
        Action act = () => OnnxMatMulNBitsQuantizer.Process(model, options);

        //Assert
        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("Gather");
    }

    [Fact]
    public async Task Process_refuses_a_bit_width_outside_the_managed_scope()
    {
        //Arrange
        OnnxModel model = await LoadAsync("matmul_k64_n8_fp32.onnx");

        //Act
        Action act = () =>
            OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 3, 32, false, null)));

        //Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task Process_refuses_a_weight_still_held_in_a_side_file()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("external_data_fp32.onnx"),
            TestContext.Current.CancellationToken);

        //Act
        Action act = () =>
            OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));

        //Assert
        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("side file");
    }

    [Fact]
    public async Task Process_quantizes_a_weight_that_arrived_in_a_side_file_once_it_is_loaded()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("external_data_fp32.onnx"),
            TestContext.Current.CancellationToken);
        await model.LoadExternalDataAsync(TestContext.Current.CancellationToken);

        //Act
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(new OnnxWeightOnlyFixtureMode("x", 4, 32, false, null)));

        //Assert
        model.Graph.Nodes[0].OpType.Should().Be("MatMulNBits");
        model.Graph.FindInitializer("weight_Q4").RawData.Should().HaveCount(2 * 6 * 16);
    }

    private static async Task<IReadOnlyList<string>> CompareAsync(
        string inputName,
        string expectedName,
        OnnxWeightOnlyFixtureMode mode)
    {
        OnnxModel model = await LoadAsync(inputName);
        OnnxModel expected = await LoadAsync(expectedName);
        OnnxMatMulNBitsQuantizer.Process(model, BuildOptions(mode));
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

    private static OnnxWeightOnlyQuantizationOptions BuildOptions(OnnxWeightOnlyFixtureMode mode) =>
        new OnnxWeightOnlyQuantizationOptions
        {
            Bits = mode.Bits,
            BlockSize = mode.BlockSize,
            IsSymmetric = mode.IsSymmetric,
            AccuracyLevel = mode.AccuracyLevel,
        };
}
