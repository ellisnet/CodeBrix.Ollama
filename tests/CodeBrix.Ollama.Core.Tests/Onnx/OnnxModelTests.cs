using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Covers the in-house ONNX codec: reading and writing every checked-in fixture byte for byte, the unmodelled fields
/// a file may carry, the typed tensor fields, and tensors kept in a side file.
/// </summary>
public sealed class OnnxModelTests
{
    [Fact]
    public async Task ReadAsync_round_trips_every_fixture_byte_for_byte()
    {
        //Arrange
        List<string> drifted = new List<string>();

        //Act
        foreach (string name in OnnxFixtureFiles.AllModels())
        {
            byte[] original = await File.ReadAllBytesAsync(
                OnnxFixtureFiles.FullPath(name),
                TestContext.Current.CancellationToken);
            OnnxModel model = OnnxModel.Parse(original, OnnxFixtureFiles.Directory);
            if (!model.Serialize().SequenceEqual(original))
            {
                drifted.Add(name);
            }
        }

        //Assert
        drifted.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_round_trips_a_model_whose_fields_the_codec_does_not_model()
    {
        //Arrange
        string path = OnnxFixtureFiles.FullPath("unknown_fields_fp32.onnx");
        byte[] original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        //Act
        OnnxModel model = await OnnxModel.ReadAsync(path, TestContext.Current.CancellationToken);

        //Assert
        model.Graph.Nodes.Should().HaveCount(1);
        model.Proto.GetMetadataValue("codebrix.note").Should().Be("carried through opaquely");
        model.Serialize().Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task ReadAsync_reads_a_tensor_held_in_a_side_file()
    {
        //Arrange
        string path = OnnxFixtureFiles.FullPath("external_data_fp32.onnx");

        //Act
        OnnxModel model = await OnnxModel.ReadAsync(path, TestContext.Current.CancellationToken);
        OnnxTensorProto weight = model.Graph.FindInitializer("weight");
        byte[] bytes = await model.ReadTensorBytesAsync(weight, TestContext.Current.CancellationToken);

        //Assert
        weight.HasExternalData.Should().BeTrue();
        weight.GetExternalDataValue("location").Should().Be("external_data_fp32.onnx.data");
        weight.GetExternalDataValue("offset").Should().Be("0");
        weight.GetExternalDataValue("length").Should().Be("1536");
        bytes.Should().HaveCount(64 * 6 * 4);
    }

    [Fact]
    public async Task LoadExternalDataAsync_brings_the_side_file_bytes_into_the_model()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("external_data_fp32.onnx"),
            TestContext.Current.CancellationToken);
        OnnxModel plain = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"),
            TestContext.Current.CancellationToken);

        //Act
        await model.LoadExternalDataAsync(TestContext.Current.CancellationToken);
        OnnxTensorProto weight = model.Graph.FindInitializer("weight");

        //Assert
        weight.HasExternalData.Should().BeFalse();
        weight.ExternalData.Should().BeEmpty();
        weight.RawData.Should().HaveCount(64 * 6 * 4);
        plain.Graph.FindInitializer("weight").RawData.Should().HaveCount(64 * 8 * 4);
    }

    [Fact]
    public async Task WriteAsync_moves_a_large_tensor_out_to_a_side_file_and_reads_it_back()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"),
            TestContext.Current.CancellationToken);
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "externalized.onnx");

        //Act
        await model.WriteAsync(
            path,
            new OnnxSaveOptions { UseExternalData = true, SizeThreshold = 1024 },
            TestContext.Current.CancellationToken);
        OnnxModel reloaded = await OnnxModel.ReadAsync(path, TestContext.Current.CancellationToken);
        OnnxTensorProto weight = reloaded.Graph.FindInitializer("weight");
        byte[] bytes = await reloaded.ReadTensorBytesAsync(weight, TestContext.Current.CancellationToken);

        //Assert
        File.Exists(Path.Combine(directory, "externalized.onnx.data")).Should().BeTrue();
        weight.HasExternalData.Should().BeTrue();
        weight.GetExternalDataValue("location").Should().Be("externalized.onnx.data");
        weight.GetExternalDataValue("offset").Should().Be("0");
        weight.GetExternalDataValue("length").Should().Be("2048");
        bytes.Should().HaveCount(64 * 8 * 4);
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task WriteAsync_resolves_a_side_file_tensor_when_it_writes_one_whole_file()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("external_data_fp32.onnx"),
            TestContext.Current.CancellationToken);
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "inlined.onnx");

        //Act
        await model.WriteAsync(path, null, TestContext.Current.CancellationToken);
        OnnxModel reloaded = await OnnxModel.ReadAsync(path, TestContext.Current.CancellationToken);
        OnnxTensorProto weight = reloaded.Graph.FindInitializer("weight");

        //Assert
        Directory.GetFiles(directory).Should().HaveCount(1);
        weight.HasExternalData.Should().BeFalse();
        weight.RawData.Should().HaveCount(64 * 6 * 4);
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task WriteAsync_writes_a_model_the_test_built_in_memory_and_reads_it_back()
    {
        //Arrange
        OnnxModelProto proto = BuildTinyModel();
        OnnxModel model = OnnxModel.FromProto(proto, null);
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "in-memory.onnx");

        //Act
        await model.WriteAsync(path, null, TestContext.Current.CancellationToken);
        OnnxModel reloaded = await OnnxModel.ReadAsync(path, TestContext.Current.CancellationToken);

        //Assert
        OnnxModelComparison.Compare(proto, reloaded.Proto).Should().BeEmpty();
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task WriteAsync_to_a_stream_produces_the_same_bytes_as_the_file()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("two_matmuls_fp32.onnx"),
            TestContext.Current.CancellationToken);
        byte[] original = await File.ReadAllBytesAsync(
            OnnxFixtureFiles.FullPath("two_matmuls_fp32.onnx"),
            TestContext.Current.CancellationToken);

        //Act
        using MemoryStream stream = new MemoryStream();
        await model.WriteAsync(stream, TestContext.Current.CancellationToken);

        //Assert
        stream.ToArray().Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task ReadAsync_reads_the_raw_bytes_of_every_data_type_the_fixtures_use()
    {
        //Arrange
        OnnxModel weightOnly = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.nbits_b4_bs32_asym.onnx"),
            TestContext.Current.CancellationToken);
        OnnxModel halfPrecision = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("matmul_k64_n4_fp16.nbits_b4_bs32_asym.onnx"),
            TestContext.Current.CancellationToken);
        OnnxModel dynamicInt8 = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.dyn_i8.onnx"),
            TestContext.Current.CancellationToken);

        //Act
        OnnxTensorProto packed = weightOnly.Graph.FindInitializer("weight_Q4");
        OnnxTensorProto scales = weightOnly.Graph.FindInitializer("weight_scales");
        OnnxTensorProto halfScales = halfPrecision.Graph.FindInitializer("weight_scales");
        OnnxTensorProto weight = dynamicInt8.Graph.FindInitializer("weight_quantized");
        OnnxTensorProto scale = dynamicInt8.Graph.FindInitializer("weight_scale");
        OnnxTensorProto zeroPoint = dynamicInt8.Graph.FindInitializer("weight_zero_point");

        //Assert
        packed.DataType.Should().Be((int)OnnxTensorDataType.UInt8);
        packed.RawData.Should().HaveCount(8 * 2 * 16);
        scales.DataType.Should().Be((int)OnnxTensorDataType.Float);
        scales.RawData.Should().HaveCount(8 * 2 * 4);
        halfScales.DataType.Should().Be((int)OnnxTensorDataType.Float16);
        halfScales.RawData.Should().HaveCount(4 * 2 * 2);
        weight.DataType.Should().Be((int)OnnxTensorDataType.Int8);
        weight.RawData.Should().HaveCount(64 * 8);
        scale.FloatData.Should().HaveCount(1);
        scale.RawData.Should().BeNull();
        zeroPoint.Int32Data.Should().HaveCount(1);
        zeroPoint.Int32Data[0].Should().Be(0);
    }

    [Fact]
    public void Parse_rejects_a_file_that_is_not_a_model()
    {
        //Arrange
        byte[] nonsense = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        //Act
        Action act = () => OnnxModel.Parse(nonsense, null);

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task ReadTensorBytesAsync_rejects_a_side_file_reference_that_points_past_the_end()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("external_data_fp32.onnx"),
            TestContext.Current.CancellationToken);
        OnnxTensorProto weight = model.Graph.FindInitializer("weight");
        weight.ExternalData[1].Value = "4096";

        //Act
        Func<Task> act = async () =>
            await model.ReadTensorBytesAsync(weight, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private static OnnxModelProto BuildTinyModel()
    {
        OnnxGraphProto graph = new OnnxGraphProto { Name = "in-memory" };
        graph.Initializers.Add(BuildRawTensor(
            "weight",
            OnnxTensorDataType.Float,
            new long[] { 2, 2 },
            new byte[] { 0, 0, 128, 63, 0, 0, 0, 64, 0, 0, 64, 64, 0, 0, 128, 64 }));
        OnnxNodeProto node = new OnnxNodeProto { OpType = "MatMul", Name = "matmul" };
        node.Inputs.Add("input");
        node.Inputs.Add("weight");
        node.Outputs.Add("output");
        graph.Nodes.Add(node);
        graph.Inputs.Add(BuildValue("input", 2));
        graph.Outputs.Add(BuildValue("output", 2));
        OnnxModelProto proto = new OnnxModelProto
        {
            IrVersion = 9,
            ProducerName = "codebrix-tests",
            Graph = graph,
        };
        proto.OpsetImports.Add(new OnnxOperatorSetId { Domain = string.Empty, Version = 17 });
        proto.MetadataProperties.Add(OnnxStringStringEntry.Create("codebrix.kind", "in-memory"));
        return proto;
    }

    //Built here rather than through the quantizer's tensor helper, which stayed with the quantizer in
    //CodeBrix.Ollama.ModelManager: nothing in this project may reach into a library the codec knows nothing
    //about, and a tensor of raw bytes is four lines.
    private static OnnxTensorProto BuildRawTensor(string name, OnnxTensorDataType dataType,
        IReadOnlyList<long> dimensions, byte[] rawData)
    {
        OnnxTensorProto tensor = new OnnxTensorProto
        {
            Name = name,
            DataType = (int)dataType,
            RawData = rawData,
        };
        foreach (long dimension in dimensions)
        {
            tensor.Dimensions.Add(dimension);
        }

        return tensor;
    }

    private static OnnxValueInfoProto BuildValue(string name, long columns)
    {
        OnnxTensorShapeProto shape = new OnnxTensorShapeProto();
        shape.Dimensions.Add(new OnnxTensorShapeDimension { DimensionParameter = "m" });
        shape.Dimensions.Add(new OnnxTensorShapeDimension { DimensionValue = columns });
        return new OnnxValueInfoProto
        {
            Name = name,
            Type = new OnnxTypeProto
            {
                TensorType = new OnnxTensorTypeProto
                {
                    ElementType = (int)OnnxTensorDataType.Float,
                    Shape = shape,
                },
            },
        };
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-onnx-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
