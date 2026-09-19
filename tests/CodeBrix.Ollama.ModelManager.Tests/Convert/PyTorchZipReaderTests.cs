using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the PyTorch zip checkpoint reader: what it reads out of a real fixture, that it agrees tensor for
/// tensor with the safetensors twin of the same weights, and what it refuses when the archive or the pickle
/// inside it says something that cannot be true.
/// </summary>
public sealed class PyTorchZipReaderTests
{
    [Fact]
    public async Task OpenAsync_lists_the_tensors_a_fixture_holds()
    {
        //Arrange
        string path = Path.Combine(ConvertFixtureFiles.CheckpointPath("tinyllamabin-123k"),
            "pytorch_model.bin");

        //Act
        using var reader = await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        reader.Tensors.Should().HaveCount(21);
        reader.ArchivePrefix.Should().Be("pytorch_model/");
        reader.Tensors[0].Name.Should().Be("model.embed_tokens.weight");
        reader.Tensors[0].DataType.Should().Be(CheckpointDataType.BF16);
        reader.Tensors[0].Shape.Should().BeEquivalentTo(new long[] { 320, 64 });
    }

    [Fact]
    public async Task OpenTensorAsync_reads_the_same_values_as_the_safetensors_twin()
    {
        //Arrange
        (string safetensorsVariant, string pickleVariant) = ConvertFixtureFiles.EquivalentPair;
        using ICheckpointReader safetensors = await SafetensorsReader.OpenAsync(
            Path.Combine(ConvertFixtureFiles.CheckpointPath(safetensorsVariant), "model.safetensors"),
            TestContext.Current.CancellationToken);
        using ICheckpointReader pickle = await PyTorchZipReader.OpenAsync(
            Path.Combine(ConvertFixtureFiles.CheckpointPath(pickleVariant), "pytorch_model.bin"),
            TestContext.Current.CancellationToken);
        var drifted = new List<string>();

        //Act
        foreach (CheckpointTensor expected in safetensors.Tensors)
        {
            CheckpointTensor actual = pickle.Tensors.FirstOrDefault(
                tensor => string.Equals(tensor.Name, expected.Name, StringComparison.Ordinal));
            if (actual == null)
            {
                drifted.Add(expected.Name + ": missing from the pickle");
                continue;
            }

            if (actual.DataType != expected.DataType || !actual.Shape.SequenceEqual(expected.Shape))
            {
                drifted.Add(expected.Name + ": the descriptors differ");
                continue;
            }

            byte[] left = await ReadAsync(safetensors, expected);
            byte[] right = await ReadAsync(pickle, actual);
            if (!left.SequenceEqual(right))
            {
                drifted.Add(expected.Name + ": the values differ");
            }
        }

        //Assert
        drifted.Should().BeEmpty();
        pickle.Tensors.Should().HaveCount(safetensors.Tensors.Count);
    }

    [Fact]
    public async Task OpenAsync_refuses_a_file_that_is_not_a_zip_archive()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = scratch.Combine("not-a-zip.bin");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("this is not a zip archive"),
            TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("not a zip archive");
    }

    [Fact]
    public async Task OpenAsync_refuses_an_archive_with_no_pickle()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteArchive(scratch, new Dictionary<string, byte[]>
        {
            ["archive/version"] = Encoding.UTF8.GetBytes("3\n"),
        });

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("no data.pkl entry");
    }

    [Fact]
    public async Task OpenAsync_refuses_an_archive_written_on_a_big_endian_machine()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteArchive(scratch, new Dictionary<string, byte[]>
        {
            ["archive/data.pkl"] = OneTensorPickle(6),
            ["archive/byteorder"] = Encoding.UTF8.GetBytes("big"),
            ["archive/data/0"] = new byte[24],
        });

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("endian machine");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_tensor_whose_storage_is_not_in_the_archive()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteArchive(scratch, new Dictionary<string, byte[]>
        {
            ["archive/data.pkl"] = OneTensorPickle(6),
            ["archive/byteorder"] = Encoding.UTF8.GetBytes("little"),
        });

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("which the archive does not hold");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_tensor_that_runs_past_the_end_of_its_storage()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteArchive(scratch, new Dictionary<string, byte[]>
        {
            ["archive/data.pkl"] = OneTensorPickle(4),
            ["archive/byteorder"] = Encoding.UTF8.GetBytes("little"),
            ["archive/data/0"] = new byte[16],
        });

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("of a storage that holds");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_tensor_that_is_not_contiguous()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteArchive(scratch, new Dictionary<string, byte[]>
        {
            ["archive/data.pkl"] = OneTensorPickle(6, strideRow: 1, strideColumn: 2),
            ["archive/byteorder"] = Encoding.UTF8.GetBytes("little"),
            ["archive/data/0"] = new byte[24],
        });

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("is not contiguous");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_pickle_that_does_not_describe_a_state_dictionary()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string path = WriteArchive(scratch, new Dictionary<string, byte[]>
        {
            ["archive/data.pkl"] = new PickleBuilder().Protocol().Unicode("not a state dictionary").Stop(),
            ["archive/byteorder"] = Encoding.UTF8.GetBytes("little"),
        });

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("does not describe a dictionary of tensors");
    }

    [Fact]
    public async Task OpenAsync_refuses_an_archive_whose_pickle_runs_an_operating_system_call()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        byte[] hostile = new PickleBuilder()
            .Protocol()
            .Global("os", "system")
            .Mark()
            .Unicode("touch /tmp/never-written")
            .Tuple()
            .Reduce()
            .Stop();
        string path = WriteArchive(scratch, new Dictionary<string, byte[]>
        {
            ["archive/data.pkl"] = hostile,
            ["archive/byteorder"] = Encoding.UTF8.GetBytes("little"),
        });

        //Act
        Func<Task> act = async () =>
            await PyTorchZipReader.OpenAsync(path, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<PickleRefusedException>()).Which.Construct.Should().Be("os.system");
    }

    private static async Task<byte[]> ReadAsync(ICheckpointReader reader, CheckpointTensor tensor)
    {
        using Stream values = await reader.OpenTensorAsync(tensor, TestContext.Current.CancellationToken);
        using var buffer = new MemoryStream();
        await values.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        return buffer.ToArray();
    }

    private static string WriteArchive(TempScratchDirectory scratch, Dictionary<string, byte[]> entries)
    {
        string path = scratch.Combine("checkpoint.bin");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (KeyValuePair<string, byte[]> entry in entries)
            {
                ZipArchiveEntry created = archive.CreateEntry(entry.Key, CompressionLevel.NoCompression);
                using Stream content = created.Open();
                content.Write(entry.Value, 0, entry.Value.Length);
            }
        }

        return path;
    }

    private static byte[] OneTensorPickle(byte storageElements, byte strideRow = 3, byte strideColumn = 1)
    {
        return new PickleBuilder()
            .Protocol()
            .EmptyDict()
            .Mark()
            .Unicode("weight")
            .Global("torch._utils", "_rebuild_tensor_v2")
            .Mark()
            .Mark()
            .Unicode("storage")
            .Global("torch", "FloatStorage")
            .Unicode("0")
            .Unicode("cpu")
            .Int(storageElements)
            .Tuple()
            .PersistentId()
            .Int(0)
            .Mark().Int(2).Int(3).Tuple()
            .Mark().Int(strideRow).Int(strideColumn).Tuple()
            .Opcode(0x89)
            .Global("collections", "OrderedDict")
            .EmptyTuple()
            .Reduce()
            .Tuple()
            .Reduce()
            .SetItems()
            .Stop();
    }
}
