using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the safetensors container reader: what it reads out of a real fixture, and what it refuses when the
/// header says something that cannot be true.
/// </summary>
public sealed class SafetensorsReaderTests
{
    [Fact]
    public async Task OpenAsync_lists_the_tensors_a_fixture_holds()
    {
        //Arrange
        string path = Path.Combine(ConvertFixtureFiles.CheckpointPath("tinyllama-123k"), "model.safetensors");

        //Act
        using ICheckpointReader reader = await SafetensorsReader.OpenAsync(path,
            TestContext.Current.CancellationToken);

        //Assert
        reader.Tensors.Should().HaveCount(21);
        CheckpointTensor embeddings = reader.Tensors.First(
            tensor => tensor.Name == "model.embed_tokens.weight");
        embeddings.DataType.Should().Be(CheckpointDataType.BF16);
        embeddings.Shape.Should().BeEquivalentTo(new long[] { 320, 64 });
        embeddings.ByteCount.Should().Be(320 * 64 * 2);
    }

    [Fact]
    public async Task OpenTensorAsync_hands_out_exactly_the_tensor_it_was_asked_for()
    {
        //Arrange
        string path = Path.Combine(ConvertFixtureFiles.CheckpointPath("tinyllama-123k"), "model.safetensors");
        using ICheckpointReader reader = await SafetensorsReader.OpenAsync(path,
            TestContext.Current.CancellationToken);
        CheckpointTensor tensor = reader.Tensors.First(entry => entry.Name == "model.norm.weight");

        //Act
        using Stream values = await reader.OpenTensorAsync(tensor, TestContext.Current.CancellationToken);
        using var buffer = new MemoryStream();
        await values.CopyToAsync(buffer, TestContext.Current.CancellationToken);

        //Assert
        buffer.Length.Should().Be(tensor.ByteCount);
        values.ReadByte().Should().Be(-1);
    }

    [Fact]
    public async Task OpenAsync_refuses_a_dtype_it_does_not_know()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteContainer(scratch, "{\"a\":{\"dtype\":\"F8_E4M3\",\"shape\":[2],\"data_offsets\":[0,2]}}",
            new byte[2]);

        //Act
        Func<Task> act = async () =>
            await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message.Should().Contain("F8_E4M3");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_byte_range_that_runs_past_the_end_of_the_file()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteContainer(scratch, "{\"a\":{\"dtype\":\"F32\",\"shape\":[8],\"data_offsets\":[0,32]}}",
            new byte[16]);

        //Act
        Func<Task> act = async () =>
            await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("data region");
    }

    [Fact]
    public async Task OpenAsync_refuses_two_tensors_that_overlap()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteContainer(scratch,
            "{\"a\":{\"dtype\":\"F32\",\"shape\":[4],\"data_offsets\":[0,16]},"
            + "\"b\":{\"dtype\":\"F32\",\"shape\":[4],\"data_offsets\":[8,24]}}",
            new byte[32]);

        //Act
        Func<Task> act = async () =>
            await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("inside the range of the tensor before it");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_shape_that_does_not_account_for_the_bytes_it_claims()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteContainer(scratch, "{\"a\":{\"dtype\":\"F32\",\"shape\":[2],\"data_offsets\":[0,16]}}",
            new byte[16]);

        //Act
        Func<Task> act = async () =>
            await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("account for");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_header_larger_than_the_cap()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = scratch.Combine("huge.safetensors");
        var content = new byte[64];
        BitConverter.GetBytes(SafetensorsReader.MaxHeaderLength + 1).CopyTo(content, 0);
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = async () =>
            await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("more than the maximum");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_header_that_is_not_json()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteContainer(scratch, "this is not json", new byte[4]);

        //Act
        Func<Task> act = async () =>
            await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("does not start with a JSON header");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_file_too_short_for_the_header_it_declares()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = scratch.Combine("short.safetensors");
        var content = new byte[16];
        BitConverter.GetBytes(1024L).CopyTo(content, 0);
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = async () =>
            await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("too short for the header");
    }

    [Fact]
    public async Task OpenAsync_reads_the_header_metadata_and_skips_it_as_a_tensor()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteContainer(scratch,
            "{\"__metadata__\":{\"format\":\"pt\"},"
            + "\"a\":{\"dtype\":\"F32\",\"shape\":[2],\"data_offsets\":[0,8]}}",
            new byte[8]);

        //Act
        using var reader = await SafetensorsReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        reader.Tensors.Should().HaveCount(1);
        reader.HeaderMetadata["format"].Should().Be("pt");
    }

    private static string WriteContainer(TempScratchDirectory scratch, string header, byte[] data)
    {
        string path = scratch.Combine("container.safetensors");
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            stream.Write(BitConverter.GetBytes((long)headerBytes.Length), 0, 8);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(data, 0, data.Length);
        }

        return path;
    }
}
