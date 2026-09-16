// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/gguf.go at commit a43fad18.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers reading a GGUF header end to end: every value type, architecture-qualified lookup, omitted arrays,
/// alignment, the legacy encodings, the derived counts, and every error the reader raises.
/// </summary>
public sealed class GgufMetadataTests
{
    private const int MaxStringLength = 16 << 20;
    private const int MaxArrayElements = 64 << 20;

    [Fact]
    public async Task ReadAsync_ReadsEveryScalarType()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt8("llama.u8", 8)
            .AddInt8("llama.i8", -8)
            .AddUInt16("llama.u16", 16)
            .AddInt16("llama.i16", -16)
            .AddUInt32("llama.u32", 32)
            .AddInt32("llama.i32", -32)
            .AddUInt64("llama.u64", 64)
            .AddInt64("llama.i64", -64)
            .AddFloat32("llama.f32", 1.5f)
            .AddFloat64("llama.f64", 2.5d)
            .AddBool("llama.flag", true);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.Version.Should().Be(3u);
        metadata.IsBigEndian.Should().BeFalse();
        metadata.GetValue("u8").Type.Should().Be(GgufValueType.UInt8);
        metadata.GetValue("u8").AsUInt64().Should().Be(8UL);
        metadata.GetValue("i8").Type.Should().Be(GgufValueType.Int8);
        metadata.GetValue("i8").AsInt64().Should().Be(-8L);
        metadata.GetValue("u16").AsUInt64().Should().Be(16UL);
        metadata.GetValue("i16").AsInt64().Should().Be(-16L);
        metadata.GetValue("u32").AsUInt64().Should().Be(32UL);
        metadata.GetValue("i32").AsInt64().Should().Be(-32L);
        metadata.GetValue("u64").AsUInt64().Should().Be(64UL);
        metadata.GetValue("i64").AsInt64().Should().Be(-64L);
        metadata.GetValue("f32").AsDouble().Should().Be(1.5d);
        metadata.GetValue("f64").AsDouble().Should().Be(2.5d);
        metadata.GetValue("flag").AsBoolean().Should().BeTrue();
        metadata.GetBool("flag").Should().BeTrue();
        metadata.Architecture.Should().Be("llama");
    }

    [Fact]
    public async Task ReadAsync_ReadsEveryArrayType()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt8Array("llama.u8s", 1, 2)
            .AddInt8Array("llama.i8s", -1, 2)
            .AddUInt16Array("llama.u16s", 1, 2)
            .AddInt16Array("llama.i16s", -1, 2)
            .AddUInt32Array("llama.u32s", 1, 2)
            .AddInt32Array("llama.i32s", -1, 2)
            .AddUInt64Array("llama.u64s", 1, 2)
            .AddInt64Array("llama.i64s", -1, 2)
            .AddFloat32Array("llama.f32s", 0f, 1f)
            .AddFloat64Array("llama.f64s", 0d, 1d)
            .AddBoolArray("llama.flags", true, false)
            .AddStringArray("tokenizer.ggml.tokens", "hello", "world");

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.GetValue("u8s").AsUInt64Array().Should().Equal(new ulong[] { 1, 2 });
        metadata.GetValue("i8s").AsInt64Array().Should().Equal(new[] { -1L, 2L });
        metadata.GetValue("u16s").AsUInt64Array().Should().Equal(new ulong[] { 1, 2 });
        metadata.GetValue("i16s").AsInt64Array().Should().Equal(new[] { -1L, 2L });
        metadata.GetValue("u32s").AsUInt64Array().Should().Equal(new ulong[] { 1, 2 });
        metadata.GetValue("i32s").AsInt64Array().Should().Equal(new[] { -1L, 2L });
        metadata.GetValue("u64s").AsUInt64Array().Should().Equal(new ulong[] { 1, 2 });
        metadata.GetValue("i64s").AsInt64Array().Should().Equal(new[] { -1L, 2L });
        metadata.GetValue("f32s").AsDoubleArray().Should().Equal(new[] { 0d, 1d });
        metadata.GetValue("f64s").AsDoubleArray().Should().Equal(new[] { 0d, 1d });
        metadata.GetValue("flags").AsBooleanArray().Should().Equal(new[] { true, false });
        metadata.GetValue("tokenizer.ggml.tokens").AsStringArray().Should().Equal(new[] { "hello", "world" });
        metadata.GetValue("tokenizer.ggml.tokens").ArrayLength.Should().Be(2L);
        metadata.OmittedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_KeepsKeysAndTensorsInFileOrder()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("llama.block_count", 2)
            .AddUInt32("llama.embedding_length", 3)
            .AddString("tokenizer.ggml.model", "gpt2")
            .AddTensor("token_embd.weight", GgufTensorType.F32, 2, 3)
            .AddTensor("blk.0.attn_q.weight", GgufTensorType.F32, 3, 3)
            .AddTensor("blk.1.attn_q.weight", GgufTensorType.F32, 3, 3);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.Keys.Should().Equal(new[]
        {
            "general.architecture", "llama.block_count", "llama.embedding_length", "tokenizer.ggml.model"
        });
        metadata.KeyValues.Should().HaveCount(4);
        metadata.Tensors.Should().HaveCount(3);
        metadata.Tensors[0].Name.Should().Be("token_embd.weight");
        metadata.Tensors[2].Name.Should().Be("blk.1.attn_q.weight");
        metadata.GetTensor("blk.0.attn_q.weight").Should().NotBeNull();
        metadata.GetTensor("does.not.exist").Should().BeNull();
        metadata.GetTensors("blk.").Should().HaveCount(2);
        metadata.GetTensors(null).Should().HaveCount(3);
        metadata.GetTensors("nothing.").Should().BeEmpty();
    }

    [Fact]
    public async Task GetValue_QualifiesKeysByArchitecture()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("llama.block_count", 8)
            .AddUInt32("tokenizer.ggml.eos_token_id", 0);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.GetUInt64("block_count").Should().Be(8UL);
        metadata.BlockCount.Should().Be(8UL);
        metadata.Has("block_count").Should().BeTrue();
        metadata.GetExactValue("block_count").Should().BeNull();
        metadata.GetExactValue("llama.block_count").Should().NotBeNull();
        metadata.GetValue("tokenizer.ggml.eos_token_id").Should().NotBeNull();
        metadata.GetValue("does.not.exist").Should().BeNull();
        metadata.Has("does.not.exist").Should().BeFalse();
    }

    [Fact]
    public async Task GetUInt64_ReadsSplitKeys_BothExactAndQualified()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("split.count", 2)
            .AddInt32("llama.split.no", 1);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.GetUInt64("split.count").Should().Be(2UL);
        metadata.GetUInt64("split.no").Should().Be(1UL);
    }

    [Fact]
    public async Task ReadAsync_SkipsArraysLongerThanTheLimit_AndListsTheirKeys()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("general.file_type", (uint)GgufFileType.Q4_K_M)
            .AddUInt32("llama.block_count", 2)
            .AddUInt32("llama.context_length", 4096)
            .AddUInt32("llama.embedding_length", 128)
            .AddUInt32Array("llama.attention.head_count", 4, 8)
            .AddUInt32Array("llama.attention.head_count_kv", 2, 4)
            .AddStringArray("tokenizer.ggml.tokens", "zero", "one", "two")
            .AddFloat32Array("tokenizer.ggml.scores", 0f, 1f, 2f)
            .AddString("tokenizer.ggml.model", "gpt2")
            .AddTensor("blk.0.attn_q.weight", GgufTensorType.F32, 2, 3);

        //Act
        GgufMetadata metadata = await ReadAsync(builder, new GgufReadOptions { MaxArraySize = 2 });

        //Assert
        metadata.GetValue("tokenizer.ggml.tokens").AsStringArray().Should().BeNull();
        metadata.GetValue("tokenizer.ggml.tokens").IsOmittedArray.Should().BeTrue();
        metadata.GetValue("tokenizer.ggml.tokens").ArrayLength.Should().Be(3L);
        metadata.GetValue("tokenizer.ggml.scores").AsDoubleArray().Should().BeNull();
        metadata.GetString("tokenizer.ggml.model").Should().Be("gpt2");
        metadata.ContextLength.Should().Be(4096UL);
        metadata.EmbeddingLength.Should().Be(128UL);
        metadata.HeadCountMax.Should().Be(8UL);
        metadata.HeadCountKvMin.Should().Be(2UL);
        metadata.ParameterCount.Should().Be(6UL);
        metadata.TensorDataSize.Should().Be(24UL);
        metadata.FileType.Should().Be(GgufFileType.Q4_K_M);
        metadata.FileTypeName.Should().Be("Q4_K_M");
        metadata.OmittedKeys.Should().HaveCount(2);
        metadata.OmittedKeys.Should().Contain("tokenizer.ggml.tokens");
        metadata.OmittedKeys.Should().Contain("tokenizer.ggml.scores");
    }

    [Fact]
    public async Task ReadAsync_RetainsEveryArray_WhenTheLimitIsNegative()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddStringArray("tokenizer.ggml.tokens", "zero", "one", "two");

        //Act
        GgufMetadata metadata = await ReadAsync(builder, new GgufReadOptions { MaxArraySize = -1 });

        //Assert
        metadata.GetValue("tokenizer.ggml.tokens").AsStringArray().Should().HaveCount(3);
        metadata.OmittedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task HeadCounts_DefaultToOne_ForEmptyArrays()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32Array("llama.attention.head_count")
            .AddInt32Array("llama.attention.head_count_kv");

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.HeadCountMax.Should().Be(1UL);
        metadata.HeadCountKvMin.Should().Be(1UL);
    }

    [Fact]
    public async Task HeadCounts_ReadScalarValues()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("llama.attention.head_count", 12)
            .AddUInt32("llama.attention.head_count_kv", 4);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.HeadCountMax.Should().Be(12UL);
        metadata.HeadCountKvMin.Should().Be(4UL);
    }

    [Fact]
    public async Task HeadCounts_FallBackToTheDefault_ForNegativeValues()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddInt32Array("llama.attention.head_count", -2, 8);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.HeadCountMax.Should().Be(1UL);
    }

    [Fact]
    public async Task Convenience_UsesDefaults_WhenTheKeysAreAbsent()
    {
        //Arrange
        var builder = new GgufTestFileBuilder().AddUInt32("unrelated.key", 1);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.Architecture.Should().Be("unknown");
        metadata.Kind.Should().Be("unknown");
        metadata.FileType.Should().Be(GgufFileType.Unknown);
        metadata.FileTypeName.Should().Be("unknown");
        metadata.BlockCount.Should().Be(0UL);
        metadata.EmbeddingLength.Should().Be(0UL);
        metadata.ContextLength.Should().Be(0UL);
        metadata.ChatTemplate.Should().BeNull();
        metadata.ParameterCount.Should().Be(0UL);
        metadata.TensorDataSize.Should().Be(0UL);
        metadata.Tensors.Should().BeEmpty();
        metadata.GetString("missing", "fallback").Should().Be("fallback");
        metadata.GetUInt64("missing", 7).Should().Be(7UL);
        metadata.GetBool("missing", true).Should().BeTrue();
    }

    [Fact]
    public async Task Convenience_ReadsTheGeneralKeysAndTheChatTemplate()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddString("general.type", "model")
            .AddUInt32("general.file_type", (uint)GgufFileType.BF16)
            .AddString("tokenizer.chat_template", "{{ .Prompt }}");

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.Kind.Should().Be("model");
        metadata.FileType.Should().Be(GgufFileType.BF16);
        metadata.FileTypeName.Should().Be("BF16");
        metadata.ChatTemplate.Should().Be("{{ .Prompt }}");
    }

    [Fact]
    public async Task ReadAsync_DefaultsAlignmentToThirtyTwo()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddTensor("blk.0.attn_q.weight", GgufTensorType.F32, 8);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.Alignment.Should().Be(32L);
        (metadata.TensorDataOffset % 32).Should().Be(0L);
        metadata.TensorDataSize.Should().Be(32UL);
        metadata.FileSize.Should().Be(metadata.TensorDataOffset + 32L);
    }

    [Fact]
    public async Task ReadAsync_UsesAnUnsignedDeclaredAlignment()
    {
        //Arrange
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("general.alignment", 64)
            .AddTensor("blk.0.attn_q.weight", GgufTensorType.F32, 8);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.Alignment.Should().Be(64L);
        (metadata.TensorDataOffset % 64).Should().Be(0L);
    }

    [Fact]
    public async Task ReadAsync_ReadsABigEndianFile()
    {
        //Arrange
        var builder = new GgufTestFileBuilder(3, true)
            .AddString("general.architecture", "llama")
            .AddUInt32("llama.block_count", 7)
            .AddFloat32("llama.rope.freq_base", 10000f)
            .AddTensor("weight", GgufTensorType.F32, 1);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.IsBigEndian.Should().BeTrue();
        metadata.Architecture.Should().Be("llama");
        metadata.BlockCount.Should().Be(7UL);
        metadata.GetValue("rope.freq_base").AsDouble().Should().Be(10000d);
        metadata.ParameterCount.Should().Be(1UL);
    }

    [Fact]
    public async Task ReadAsync_ReadsAVersionOneFile()
    {
        //Arrange
        var builder = new GgufTestFileBuilder(1)
            .AddString("general.architecture", "llama")
            .AddTensor("weight", GgufTensorType.F32, 1);

        //Act
        GgufMetadata metadata = await ReadAsync(builder);

        //Assert
        metadata.Version.Should().Be(1u);
        metadata.Architecture.Should().Be("llama");
        metadata.ParameterCount.Should().Be(1UL);
    }

    [Fact]
    public async Task ReadAsync_SkipsOmittedArrays_InAVersionOneFile()
    {
        //Arrange
        var builder = new GgufTestFileBuilder(1)
            .AddString("general.architecture", "llama")
            .AddStringArray("tokenizer.ggml.tokens", "zero", "one", "two");

        //Act
        GgufMetadata metadata = await ReadAsync(builder, new GgufReadOptions { MaxArraySize = 2 });

        //Assert
        metadata.OmittedKeys.Should().Equal(new[] { "tokenizer.ggml.tokens" });
        metadata.Architecture.Should().Be("llama");
    }

    [Fact]
    public async Task ReadAsync_ReadsFromAPathOnDisk()
    {
        //Arrange
        string directory = CreateTempDirectory();
        try
        {
            string path = new GgufTestFileBuilder()
                .AddString("general.architecture", "llama")
                .AddUInt32("llama.block_count", 3)
                .AddTensor("weight", GgufTensorType.F32, 4)
                .BuildFile(Path.Combine(directory, "model.gguf"));

            //Act
            GgufMetadata metadata =
                await GgufMetadata.ReadAsync(path, null, TestContext.Current.CancellationToken);

            //Assert
            metadata.Architecture.Should().Be("llama");
            metadata.BlockCount.Should().Be(3UL);
            metadata.ParameterCount.Should().Be(4UL);
            metadata.FileSize.Should().BeGreaterThan(0L);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReadAsync_SkipsTensorValidation_ForAStreamWithoutALength()
    {
        //Arrange
        byte[] bytes = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddTensor("weight", GgufTensorType.F32, 8)
            .Build();
        byte[] truncated = new byte[bytes.Length - 32];
        Array.Copy(bytes, truncated, truncated.Length);

        //Act
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(Convert.ToBase64String(truncated)));
        using var stream = new CryptoStream(source, new FromBase64Transform(), CryptoStreamMode.Read);
        GgufMetadata metadata =
            await GgufMetadata.ReadAsync(stream, null, TestContext.Current.CancellationToken);

        //Assert
        stream.CanSeek.Should().BeFalse();
        metadata.FileSize.Should().Be(-1L);
        metadata.Architecture.Should().Be("llama");
        metadata.Tensors.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReadAsync_SkipsTensorValidation_WhenTheOptionIsOff()
    {
        //Arrange
        byte[] bytes = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddTensor("weight", GgufTensorType.F32, 8)
            .Build();
        byte[] truncated = new byte[bytes.Length - 32];
        Array.Copy(bytes, truncated, truncated.Length);

        //Act
        using var stream = new MemoryStream(truncated, false);
        GgufMetadata metadata = await GgufMetadata.ReadAsync(stream,
            new GgufReadOptions { ValidateTensorData = false }, TestContext.Current.CancellationToken);

        //Assert
        metadata.Tensors.Should().HaveCount(1);
        metadata.ParameterCount.Should().Be(8UL);
    }

    [Fact]
    public async Task ReadAsync_RejectsTensorDataBeyondTheEndOfTheFile()
    {
        //Arrange
        byte[] bytes = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddTensor("weight", GgufTensorType.F32, 8)
            .Build();
        byte[] truncated = new byte[bytes.Length - 32];
        Array.Copy(bytes, truncated, truncated.Length);

        //Act
        Func<Task> act = async () => await ReadBytesAsync(truncated);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsATensorOffsetThatOverflows()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 3, 1, 0);
            WriteRawString(writer, "bad.weight");
            writer.Write(1u);
            writer.Write(1UL);
            writer.Write((uint)GgufTensorType.F32);
            writer.Write(ulong.MaxValue);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAFileThatIsNotGguf()
    {
        //Arrange
        var bytes = new byte[24];
        Encoding.ASCII.GetBytes("nope").CopyTo(bytes, 0);

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAnUnsupportedVersion()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("GGUF"));
            writer.Write(0u);
            writer.Write(0UL);
            writer.Write(0UL);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAnUnsupportedValueType()
    {
        //Arrange
        byte[] bytes = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddRawTypedValue("llama.bad", 13)
            .Build();

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAnUnsupportedArrayElementType()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 3, 0, 1);
            WriteRawString(writer, "bad");
            writer.Write(9u);
            writer.Write(9u);
            writer.Write(0UL);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAZeroAlignment()
    {
        //Arrange
        byte[] bytes = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("general.alignment", 0)
            .Build();

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAnAlignmentThatIsNotAnInteger()
    {
        //Arrange
        byte[] bytes = new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddString("general.alignment", "sixty four")
            .Build();

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsTooManyTensorDimensions()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 3, 1, 0);
            WriteRawString(writer, "bad.weight");
            writer.Write(5u);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsATensorWhoseElementCountOverflows()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 3, 1, 0);
            WriteRawString(writer, "bad.weight");
            writer.Write(2u);
            writer.Write((ulong)long.MaxValue);
            writer.Write(2UL);
            writer.Write((uint)GgufTensorType.F32);
            writer.Write(0UL);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsATruncatedHeader()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("GGUF"));
            writer.Write(3u);
            writer.Write(0UL);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAnOversizedString()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 3, 0, 1);
            writer.Write((ulong)MaxStringLength + 1);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAnOversizedArray()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 3, 0, 1);
            WriteRawString(writer, "big");
            writer.Write(9u);
            writer.Write(8u);
            writer.Write((ulong)MaxArrayElements + 1);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAnItemCountLargerThanTheAddressSpace()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("GGUF"));
            writer.Write(3u);
            writer.Write(0UL);
            writer.Write(ulong.MaxValue);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_DoesNotPreallocateADeclaredItemCount()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("GGUF"));
            writer.Write(3u);
            writer.Write(0UL);
            writer.Write((ulong)int.MaxValue);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_DoesNotPreallocateADeclaredArray()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 3, 0, 1);
            WriteRawString(writer, "big");
            writer.Write(9u);
            writer.Write(6u);
            writer.Write(8_000_000UL);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes, new GgufReadOptions { MaxArraySize = -1 });

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAVersionOneStringWithZeroLength()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 1, 0, 1);
            writer.Write(0UL);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsAVersionOneStringWithoutATerminator()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 1, 0, 1);
            writer.Write(4UL);
            writer.Write(Encoding.ASCII.GetBytes("nope"));
            writer.Write(8u);
            writer.Write(1UL);
            writer.Write((byte)0);
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_RejectsASkippedVersionOneStringWithoutATerminator()
    {
        //Arrange
        byte[] bytes = RawBytes(writer =>
        {
            WriteHeader(writer, 1, 0, 1);
            WriteLegacyString(writer, "tokenizer.ggml.tokens");
            writer.Write(9u);
            writer.Write(8u);
            writer.Write(1UL);
            writer.Write(4UL);
            writer.Write(Encoding.ASCII.GetBytes("nope"));
        });

        //Act
        Func<Task> act = async () => await ReadBytesAsync(bytes, new GgufReadOptions { MaxArraySize = 0 });

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task ReadAsync_ThrowsForANullPathOrStream()
    {
        //Arrange
        Func<Task> nullPath = async () =>
            await GgufMetadata.ReadAsync((string)null, null, TestContext.Current.CancellationToken);
        Func<Task> emptyPath = async () =>
            await GgufMetadata.ReadAsync(string.Empty, null, TestContext.Current.CancellationToken);
        Func<Task> nullStream = async () =>
            await GgufMetadata.ReadAsync((Stream)null, null, TestContext.Current.CancellationToken);

        //Act
        //Assert
        await nullPath.Should().ThrowAsync<ArgumentNullException>();
        await emptyPath.Should().ThrowAsync<ArgumentException>();
        await nullStream.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReadAsync_ReadsTheConformanceTestVector()
    {
        //Arrange
        string path = Path.Combine(AppContext.BaseDirectory, "test-vectors", "codebrix-conformance-tiny.gguf");
        File.Exists(path).Should().BeTrue();

        //Act
        GgufMetadata metadata = await GgufMetadata.ReadAsync(path, null, TestContext.Current.CancellationToken);

        //Assert
        metadata.Version.Should().Be(3u);
        metadata.IsBigEndian.Should().BeFalse();
        metadata.FileSize.Should().Be(101056L);
        metadata.Alignment.Should().Be(32L);
        metadata.Architecture.Should().Be("llama");
        metadata.Keys.Should().HaveCount(15);
        metadata.Tensors.Should().HaveCount(21);
        metadata.GetString("general.name").Should().Be("codebrix-conformance-tiny");
        metadata.GetString("tokenizer.ggml.model").Should().Be("no_vocab");
        metadata.ContextLength.Should().Be(64UL);
        metadata.EmbeddingLength.Should().Be(32UL);
        metadata.BlockCount.Should().Be(2UL);
        metadata.HeadCountMax.Should().Be(4UL);
        metadata.HeadCountKvMin.Should().Be(4UL);
        metadata.GetUInt64("vocab_size").Should().Be(64UL);
        metadata.GetUInt64("feed_forward_length").Should().Be(64UL);
        metadata.FileType.Should().Be(GgufFileType.F32);
        metadata.FileTypeName.Should().Be("F32");
        metadata.OmittedKeys.Should().BeEmpty();
        metadata.GetTensors("blk.0.").Should().HaveCount(9);
        metadata.GetTensor("token_embd.weight").Type.Should().Be(GgufTensorType.F32);
        metadata.GetTensor("token_embd.weight").ElementCount.Should().Be(2048L);
        metadata.ParameterCount.Should().Be(24736UL);
        metadata.TensorDataSize.Should().Be(98944UL);
    }

    private static Task<GgufMetadata> ReadAsync(GgufTestFileBuilder builder, GgufReadOptions options = null)
    {
        return ReadBytesAsync(builder.Build(), options);
    }

    private static async Task<GgufMetadata> ReadBytesAsync(byte[] bytes, GgufReadOptions options = null)
    {
        using var stream = new MemoryStream(bytes, false);
        return await GgufMetadata.ReadAsync(stream, options, TestContext.Current.CancellationToken);
    }

    private static byte[] RawBytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            write(writer);
        }

        return stream.ToArray();
    }

    private static void WriteHeader(BinaryWriter writer, uint version, ulong tensorCount, ulong keyValueCount)
    {
        writer.Write(Encoding.ASCII.GetBytes("GGUF"));
        writer.Write(version);
        if (version == 1)
        {
            writer.Write((uint)tensorCount);
            writer.Write((uint)keyValueCount);
        }
        else
        {
            writer.Write(tensorCount);
            writer.Write(keyValueCount);
        }
    }

    private static void WriteRawString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteLegacyString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)(bytes.Length + 1));
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-ollama-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
