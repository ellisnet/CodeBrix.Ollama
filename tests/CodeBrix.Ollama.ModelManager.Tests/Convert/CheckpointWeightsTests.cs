using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the reader that opens a checkpoint's weights however many files they are in: the order the parts and
/// their tensors are visited in, which files an index decides are parts, and what a checkpoint whose index and
/// files disagree is refused with.
/// </summary>
public sealed class CheckpointWeightsTests
{
    [Fact]
    public async Task OpenAsync_visits_the_parts_in_file_name_order_and_each_part_in_its_own_order()
    {
        //Act
        using CheckpointWeights weights = await OpenAsync(Sharded);

        //Assert
        //Shard one holds the second block, shard two the model-level tensors and shard three the first block.
        //Inside a safetensors part the tensors are taken in NAME order, which is why the norm comes first.
        Names(weights).Take(3).Should().Equal(
            "model.layers.1.input_layernorm.weight",
            "model.layers.1.mlp.down_proj.weight",
            "model.layers.1.mlp.gate_proj.weight");
        Names(weights)[9].Should().Be("lm_head.weight");
        Names(weights)[12].Should().Be("model.layers.0.input_layernorm.weight");
        weights.PartPaths.Should().HaveCount(3);
        weights.IsSafetensors.Should().BeTrue();
    }

    [Fact]
    public async Task OpenAsync_keeps_a_pickle_parts_state_dictionary_order()
    {
        //Act
        using CheckpointWeights weights = await OpenAsync(ShardedPickle);

        //Assert
        Names(weights).Take(3).Should().Equal(
            "model.layers.1.self_attn.q_proj.weight",
            "model.layers.1.self_attn.k_proj.weight",
            "model.layers.1.self_attn.v_proj.weight");
        weights.IsSafetensors.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAsync_reads_a_shards_tensor_as_the_single_file_twin_holds_it()
    {
        //Arrange
        using CheckpointWeights sharded = await OpenAsync(Sharded);
        using CheckpointWeights single = await OpenAsync("tinyllama-123k");
        CheckpointTensor fromShard = sharded.Tensors.First(
            tensor => tensor.Name == "model.embed_tokens.weight");
        CheckpointTensor fromSingle = single.Tensors.First(
            tensor => tensor.Name == "model.embed_tokens.weight");

        //Act
        byte[] shardBytes = await ReadAsync(sharded, fromShard);
        byte[] singleBytes = await ReadAsync(single, fromSingle);

        //Assert
        shardBytes.Should().HaveCount((int)fromSingle.ByteCount);
        shardBytes.SequenceEqual(singleBytes).Should().BeTrue();
    }

    [Fact]
    public async Task OpenAsync_adds_the_size_of_every_part()
    {
        //Arrange
        long expected = Directory
            .GetFiles(ConvertFixtureFiles.CheckpointPath(Sharded), "*.safetensors")
            .Sum(path => new FileInfo(path).Length);

        //Act
        using CheckpointWeights weights = await OpenAsync(Sharded);

        //Assert
        weights.TotalBytes.Should().Be(expected);
    }

    [Fact]
    public async Task OpenAsync_leaves_a_file_the_index_does_not_name_alone()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Sharded);
        File.Copy(Path.Combine(checkpoint.DirectoryPath, "model-00001-of-00003.safetensors"),
            Path.Combine(checkpoint.DirectoryPath, "model-00009-of-00003.safetensors"));

        //Act
        using CheckpointWeights weights = await CheckpointWeights.OpenAsync(
            checkpoint.DirectoryPath, TestContext.Current.CancellationToken);

        //Assert
        weights.Tensors.Should().HaveCount(21);
        weights.PartPaths.Should().HaveCount(3);
    }

    [Fact]
    public async Task OpenAsync_refuses_a_tensor_that_is_in_two_parts()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Sharded);
        checkpoint.DeleteFile("model.safetensors.index.json");
        File.Copy(Path.Combine(checkpoint.DirectoryPath, "model-00001-of-00003.safetensors"),
            Path.Combine(checkpoint.DirectoryPath, "model-00004-of-00003.safetensors"));

        //Act
        Func<Task> act = async () => await CheckpointWeights.OpenAsync(
            checkpoint.DirectoryPath, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("model-00004-of-00003.safetensors");
    }

    [Fact]
    public async Task OpenAsync_refuses_an_index_that_names_a_shard_that_is_not_there()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Sharded);
        checkpoint.DeleteFile("model-00002-of-00003.safetensors");

        //Act
        Func<Task> act = async () => await CheckpointWeights.OpenAsync(
            checkpoint.DirectoryPath, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("model-00002-of-00003.safetensors");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_shard_holding_a_tensor_the_index_does_not_list()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Sharded);
        Dictionary<string, string> map = await LayoutAsync(checkpoint.DirectoryPath);
        map.Remove("model.norm.weight");
        WriteIndex(checkpoint, map);

        //Act
        Func<Task> act = async () => await CheckpointWeights.OpenAsync(
            checkpoint.DirectoryPath, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("model.norm.weight");
    }

    [Fact]
    public async Task OpenAsync_refuses_an_index_that_lists_a_tensor_no_shard_holds()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Sharded);
        Dictionary<string, string> map = await LayoutAsync(checkpoint.DirectoryPath);
        map["model.nowhere.weight"] = "model-00002-of-00003.safetensors";
        WriteIndex(checkpoint, map);

        //Act
        Func<Task> act = async () => await CheckpointWeights.OpenAsync(
            checkpoint.DirectoryPath, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("model.nowhere.weight");
    }

    [Fact]
    public async Task OpenAsync_refuses_a_folder_with_no_weights()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.DeleteFile("model.safetensors");

        //Act
        Func<Task> act = async () => await CheckpointWeights.OpenAsync(
            checkpoint.DirectoryPath, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("no weights to convert");
    }

    [Fact]
    public async Task OpenTensorAsync_refuses_a_tensor_that_is_not_this_checkpoints()
    {
        //Arrange
        using CheckpointWeights weights = await OpenAsync(Sharded);
        var stranger = new CheckpointTensor("elsewhere", CheckpointDataType.F32, new long[] { 1 }, 0);

        //Act
        Func<Task> act = async () => await weights.OpenTensorAsync(
            stranger, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("elsewhere");
    }

    [Fact]
    public void Parse_refuses_a_weight_map_that_lists_a_tensor_twice()
    {
        //Arrange
        byte[] content = Encoding.UTF8.GetBytes(
            "{\"weight_map\":{\"a.weight\":\"one.safetensors\",\"a.weight\":\"two.safetensors\"}}");

        //Act
        Action act = () => CheckpointShardIndex.Parse(content, "model.safetensors.index.json");

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message.Should().Contain("twice");
    }

    [Fact]
    public void Parse_refuses_an_index_with_no_weight_map()
    {
        //Act
        Action act = () => CheckpointShardIndex.Parse(
            Encoding.UTF8.GetBytes("{\"metadata\":{\"total_size\":1}}"), "model.safetensors.index.json");

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message.Should().Contain("weight_map");
    }

    [Fact]
    public void Parse_refuses_an_empty_weight_map()
    {
        //Act
        Action act = () => CheckpointShardIndex.Parse(
            Encoding.UTF8.GetBytes("{\"weight_map\":{}}"), "model.safetensors.index.json");

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message.Should().Contain("empty");
    }

    [Theory]
    [InlineData("../elsewhere.safetensors")]
    [InlineData("nested/shard.safetensors")]
    [InlineData("")]
    public void Parse_refuses_a_shard_name_that_is_not_a_file_beside_the_index(string shard)
    {
        //Arrange
        byte[] content = Encoding.UTF8.GetBytes(
            "{\"weight_map\":{\"a.weight\":" + JsonSerializer.Serialize(shard) + "}}");

        //Act
        Action act = () => CheckpointShardIndex.Parse(content, "model.safetensors.index.json");

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message.Should().Contain("a.weight");
    }

    [Fact]
    public void Parse_sorts_the_shard_names_and_keeps_the_tensor_order_of_the_file()
    {
        //Arrange
        byte[] content = Encoding.UTF8.GetBytes(
            "{\"weight_map\":{\"b.weight\":\"two.safetensors\",\"a.weight\":\"one.safetensors\","
            + "\"c.weight\":\"two.safetensors\"}}");

        //Act
        CheckpointShardIndex index = CheckpointShardIndex.Parse(content, "model.safetensors.index.json");

        //Assert
        index.ShardFileNames.Should().Equal("one.safetensors", "two.safetensors");
        index.TensorNames.Should().Equal("b.weight", "a.weight", "c.weight");
    }

    private static string Sharded => ConvertFixtureFiles.ShardedPair.Safetensors;

    private static string ShardedPickle => ConvertFixtureFiles.ShardedPair.Pickle;

    private static Task<CheckpointWeights> OpenAsync(string variant)
        => CheckpointWeights.OpenAsync(ConvertFixtureFiles.CheckpointPath(variant),
            TestContext.Current.CancellationToken);

    private static IReadOnlyList<string> Names(CheckpointWeights weights)
        => weights.Tensors.Select(tensor => tensor.Name).ToList();

    private static async Task<byte[]> ReadAsync(CheckpointWeights weights, CheckpointTensor tensor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using Stream values = await weights.OpenTensorAsync(tensor, cancellationToken);
        using var buffer = new MemoryStream();
        await values.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Which file each tensor of a sharded fixture is really in, read from the shards themselves.</summary>
    private static async Task<Dictionary<string, string>> LayoutAsync(string directory)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] paths = Directory.GetFiles(directory, "model-*.safetensors");
        Array.Sort(paths, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            using SafetensorsReader reader = await SafetensorsReader.OpenAsync(
                path, TestContext.Current.CancellationToken);
            foreach (CheckpointTensor tensor in reader.Tensors)
            {
                map[tensor.Name] = Path.GetFileName(path);
            }
        }

        return map;
    }

    private static void WriteIndex(TempCheckpointDirectory checkpoint, Dictionary<string, string> weightMap)
    {
        var index = new Dictionary<string, object>(StringComparer.Ordinal) { ["weight_map"] = weightMap };
        checkpoint.WriteText("model.safetensors.index.json", JsonSerializer.Serialize(index));
    }
}
