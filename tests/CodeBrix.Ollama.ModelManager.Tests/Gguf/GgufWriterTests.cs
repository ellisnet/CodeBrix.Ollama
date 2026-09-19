using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the GGUF writer: that it is the reader's exact mirror - every checked-in GGUF read and written again
/// is the same bytes - and that its own rules hold on files built by hand.
/// </summary>
public sealed class GgufWriterTests
{
    public static IEnumerable<object[]> CheckedInFiles()
    {
        yield return new object[] { Path.Combine(AppContext.BaseDirectory, "test-vectors",
            "codebrix-conformance-tiny.gguf") };
        foreach ((string variant, string outputType, GgufOutputType _) in ConvertFixtureFiles.All())
        {
            yield return new object[] { ConvertFixtureFiles.OraclePath(variant, outputType) };
        }
    }

    [Theory]
    [MemberData(nameof(CheckedInFiles))]
    public async Task WriteAsync_round_trips_a_checked_in_file_byte_for_byte(string path)
    {
        //Arrange
        byte[] original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        //Act
        byte[] rebuilt = await RebuildAsync(original);

        //Assert
        rebuilt.Length.Should().Be(original.Length);
        rebuilt.SequenceEqual(original).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_writes_every_key_value_type_the_reader_knows()
    {
        //Arrange
        var writer = new GgufWriter("llama");
        writer.AddUInt8("a.uint8", 200);
        writer.AddInt8("a.int8", -100);
        writer.AddUInt16("a.uint16", 60000);
        writer.AddInt16("a.int16", -30000);
        writer.AddUInt32("a.uint32", 4000000000);
        writer.AddInt32("a.int32", -2000000000);
        writer.AddUInt64("a.uint64", 18000000000000000000);
        writer.AddInt64("a.int64", -9000000000000000000);
        writer.AddFloat32("a.float32", 1.5f);
        writer.AddFloat64("a.float64", 2.25d);
        writer.AddBoolean("a.bool", true);
        writer.AddString("a.string", "a value");
        writer.AddArray("a.strings", GgufValueType.String, new[] { "one", "two" });
        writer.AddArray("a.int32s", GgufValueType.Int32, new[] { 1, -2, 3 });
        writer.AddArray("a.float32s", GgufValueType.Float32, new[] { 0.5f, -0.25f });
        writer.AddArray("a.bools", GgufValueType.Bool, new[] { true, false });

        //Act
        GgufMetadata metadata = await WriteAndReadAsync(writer);

        //Assert
        metadata.GetExactValue("a.uint8").AsUInt64().Should().Be(200UL);
        metadata.GetExactValue("a.int8").AsInt64().Should().Be(-100L);
        metadata.GetExactValue("a.uint16").AsUInt64().Should().Be(60000UL);
        metadata.GetExactValue("a.int16").AsInt64().Should().Be(-30000L);
        metadata.GetExactValue("a.uint32").AsUInt64().Should().Be(4000000000UL);
        metadata.GetExactValue("a.int32").AsInt64().Should().Be(-2000000000L);
        metadata.GetExactValue("a.uint64").AsUInt64().Should().Be(18000000000000000000UL);
        metadata.GetExactValue("a.int64").AsInt64().Should().Be(-9000000000000000000L);
        metadata.GetExactValue("a.float32").AsDouble().Should().Be(1.5d);
        metadata.GetExactValue("a.float64").AsDouble().Should().Be(2.25d);
        metadata.GetExactValue("a.bool").AsBoolean().Should().BeTrue();
        metadata.GetExactValue("a.string").AsString().Should().Be("a value");
        metadata.GetExactValue("a.strings").AsStringArray().Should().BeEquivalentTo(new[] { "one", "two" });
        metadata.GetExactValue("a.int32s").AsInt64Array().Should().BeEquivalentTo(new long[] { 1, -2, 3 });
        metadata.GetExactValue("a.float32s").AsDoubleArray().Should().BeEquivalentTo(new[] { 0.5d, -0.25d });
        metadata.GetExactValue("a.bools").AsBooleanArray().Should().BeEquivalentTo(new[] { true, false });
    }

    [Fact]
    public async Task WriteAsync_writes_a_file_with_no_tensors()
    {
        //Arrange
        var writer = new GgufWriter("llama");
        writer.AddString("general.name", "nothing at all");

        //Act
        GgufMetadata metadata = await WriteAndReadAsync(writer);

        //Assert
        writer.TensorCount.Should().Be(0);
        writer.KeyCount.Should().Be(2);
        metadata.Tensors.Should().BeEmpty();
        metadata.Keys.Should().BeEquivalentTo(new[] { "general.architecture", "general.name" });
    }

    [Fact]
    public async Task WriteAsync_pads_every_tensor_up_to_the_alignment()
    {
        //Arrange
        var writer = new GgufWriter("llama");
        AddZeroTensor(writer, "a", GgufTensorType.F32, 5);
        AddZeroTensor(writer, "b", GgufTensorType.F32, 3);

        //Act
        using var stream = new MemoryStream();
        long written = await writer.WriteAsync(stream, TestContext.Current.CancellationToken);
        GgufMetadata metadata = await ReadAsync(stream.ToArray());

        //Assert
        metadata.TensorDataOffset.Should().Be(GgufWriter.Pad(metadata.TensorDataOffset, 32));
        metadata.Tensors[0].Offset.Should().Be(0UL);
        metadata.Tensors[1].Offset.Should().Be(32UL);
        written.Should().Be(metadata.TensorDataOffset + 64);
    }

    [Fact]
    public async Task AddCustomAlignment_changes_where_the_tensor_data_starts()
    {
        //Arrange
        var writer = new GgufWriter("llama");
        writer.AddCustomAlignment(64);
        AddZeroTensor(writer, "a", GgufTensorType.F32, 5);
        AddZeroTensor(writer, "b", GgufTensorType.F32, 3);

        //Act
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, TestContext.Current.CancellationToken);
        GgufMetadata metadata = await ReadAsync(stream.ToArray());

        //Assert
        metadata.Alignment.Should().Be(64L);
        metadata.TensorDataOffset.Should().Be(GgufWriter.Pad(metadata.TensorDataOffset, 64));
        metadata.Tensors[1].Offset.Should().Be(64UL);
    }

    [Fact]
    public void AddCustomAlignment_refuses_an_alignment_that_is_not_a_power_of_two()
    {
        //Arrange
        var writer = new GgufWriter("llama");

        //Act
        Action act = () => writer.AddCustomAlignment(48);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AddString_leaves_out_an_empty_value_the_way_the_engine_does()
    {
        //Arrange
        var writer = new GgufWriter("llama");
        writer.AddString("general.name", string.Empty);
        writer.AddArray("general.tags", GgufValueType.String, Array.Empty<string>());

        //Act
        GgufMetadata metadata = await WriteAndReadAsync(writer);

        //Assert
        metadata.Keys.Should().BeEquivalentTo(new[] { "general.architecture" });
    }

    [Fact]
    public void AddTensor_refuses_the_same_name_twice()
    {
        //Arrange
        var writer = new GgufWriter("llama");
        AddZeroTensor(writer, "a", GgufTensorType.F32, 4);

        //Act
        Action act = () => AddZeroTensor(writer, "a", GgufTensorType.F32, 4);

        //Assert
        act.Should().Throw<GgufFormatException>().Which.Message.Should().Contain("twice");
    }

    private static void AddZeroTensor(GgufWriter writer, string name, GgufTensorType type, long elements)
    {
        long byteCount = elements * GgufTensorTypes.GetTypeSize(type);
        writer.AddTensor(new GgufWriterTensor(name, type, new[] { elements }, byteCount,
            async (destination, cancellationToken) =>
                await destination.WriteAsync(new byte[byteCount], cancellationToken)));
    }

    private static async Task<GgufMetadata> WriteAndReadAsync(GgufWriter writer)
    {
        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, TestContext.Current.CancellationToken);
        return await ReadAsync(stream.ToArray());
    }

    private static async Task<GgufMetadata> ReadAsync(byte[] content)
    {
        using var stream = new MemoryStream(content);
        return await GgufMetadata.ReadAsync(stream, null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads a GGUF file and writes it again through the writer, copying every tensor's bytes across unchanged.
    /// What comes out should be what went in, to the byte.
    /// </summary>
    private static async Task<byte[]> RebuildAsync(byte[] original)
    {
        GgufMetadata metadata = await ReadAsync(original);
        var writer = new GgufWriter(metadata.GetExactValue("general.architecture").AsString());
        foreach (string key in metadata.Keys)
        {
            if (string.Equals(key, "general.architecture", StringComparison.Ordinal))
            {
                continue;
            }

            Add(writer, key, metadata.GetExactValue(key));
        }

        foreach (GgufTensorInfo tensor in metadata.Tensors)
        {
            long start = metadata.TensorDataOffset + (long)tensor.Offset;
            long byteCount = tensor.ByteCount;
            writer.AddTensor(new GgufWriterTensor(tensor.Name, tensor.Type, tensor.Shape.Select(
                dimension => (long)dimension).ToList(), byteCount,
                async (destination, cancellationToken) =>
                    await destination.WriteAsync(
                        new ReadOnlyMemory<byte>(original, (int)start, (int)byteCount), cancellationToken)));
        }

        using var stream = new MemoryStream();
        await writer.WriteAsync(stream, TestContext.Current.CancellationToken);
        return stream.ToArray();
    }

    private static void Add(GgufWriter writer, string key, GgufValue value)
    {
        if (string.Equals(key, "general.alignment", StringComparison.Ordinal))
        {
            writer.AddCustomAlignment((uint)value.AsUInt64());
            return;
        }

        if (value.IsArray)
        {
            AddArray(writer, key, value);
            return;
        }

        switch (value.Type)
        {
            case GgufValueType.UInt8: writer.AddUInt8(key, (byte)value.RawValue); return;
            case GgufValueType.Int8: writer.AddInt8(key, (sbyte)value.RawValue); return;
            case GgufValueType.UInt16: writer.AddUInt16(key, (ushort)value.RawValue); return;
            case GgufValueType.Int16: writer.AddInt16(key, (short)value.RawValue); return;
            case GgufValueType.UInt32: writer.AddUInt32(key, (uint)value.RawValue); return;
            case GgufValueType.Int32: writer.AddInt32(key, (int)value.RawValue); return;
            case GgufValueType.UInt64: writer.AddUInt64(key, (ulong)value.RawValue); return;
            case GgufValueType.Int64: writer.AddInt64(key, (long)value.RawValue); return;
            case GgufValueType.Float32: writer.AddFloat32(key, (float)value.RawValue); return;
            case GgufValueType.Float64: writer.AddFloat64(key, (double)value.RawValue); return;
            case GgufValueType.Bool: writer.AddBoolean(key, (bool)value.RawValue); return;
            default: writer.AddString(key, (string)value.RawValue); return;
        }
    }

    private static void AddArray(GgufWriter writer, string key, GgufValue value)
    {
        writer.AddArray(key, value.ArrayElementType, (Array)value.RawValue);
    }
}
