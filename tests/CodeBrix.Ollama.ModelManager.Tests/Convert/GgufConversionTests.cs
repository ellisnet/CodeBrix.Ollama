using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Compares what the managed converter produces with what the inference engine's own converter produced from the
/// same checkpoint: the whole file byte for byte, and - when a file differs - key by key and tensor by tensor,
/// so that a failure says what drifted rather than only that something did.
/// </summary>
public sealed class GgufConversionTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        foreach ((string variant, string outputType, GgufOutputType requested) in ConvertFixtureFiles.All())
        {
            yield return new object[] { variant, outputType, requested };
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ConvertAsync_matches_the_oracle_byte_for_byte(string variant, string outputType,
        GgufOutputType requested)
    {
        //Arrange
        byte[] oracle = await File.ReadAllBytesAsync(ConvertFixtureFiles.OraclePath(variant, outputType),
            TestContext.Current.CancellationToken);

        //Act
        byte[] produced = await ConvertAsync(variant, requested);

        //Assert
        Describe(produced, oracle).Should().BeEmpty();
        produced.Length.Should().Be(oracle.Length);
        produced.SequenceEqual(oracle).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertAsync_reports_what_it_wrote()
    {
        //Arrange
        var options = new ConvertOptions { OutputType = GgufOutputType.Auto };

        //Act
        using var destination = new MemoryStream();
        ConvertResult result = await GgufConversion.ConvertAsync(
            ConvertFixtureFiles.CheckpointPath("tinyllama-123k"), destination, options,
            TestContext.Current.CancellationToken);

        //Assert
        result.Name.Should().Be("tinyllama-123k");
        result.Architecture.Should().Be(CheckpointArchitecture.Llama);
        result.TypeWritten.Should().Be(GgufOutputType.BF16);
        result.TensorCount.Should().Be(21);
        result.OutputBytes.Should().Be(destination.Length);
        result.SourceBytes.Should().BeGreaterThan(0);
        result.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
        result.ToolVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConvertAsync_reads_a_float32_checkpoint_as_f16_when_the_type_is_automatic()
    {
        //Arrange
        using var destination = new MemoryStream();

        //Act
        ConvertResult result = await GgufConversion.ConvertAsync(
            ConvertFixtureFiles.CheckpointPath("tinyllamaftt-123k"), destination, null,
            TestContext.Current.CancellationToken);

        //Assert
        result.TypeWritten.Should().Be(GgufOutputType.F16);
    }

    [Fact]
    public async Task ConvertAsync_omits_the_output_weight_when_the_embeddings_are_tied()
    {
        //Arrange
        using var destination = new MemoryStream();

        //Act
        await GgufConversion.ConvertAsync(ConvertFixtureFiles.CheckpointPath("tinyllamatied-103k"), destination,
            null, TestContext.Current.CancellationToken);
        GgufMetadata metadata = await ReadAsync(destination.ToArray());

        //Assert
        metadata.Tensors.Any(tensor => tensor.Name == "output.weight").Should().BeFalse();
        metadata.Tensors.Any(tensor => tensor.Name == "token_embd.weight").Should().BeTrue();
    }

    [Fact]
    public async Task ConvertAsync_keeps_every_one_dimensional_tensor_at_full_precision()
    {
        //Arrange
        using var destination = new MemoryStream();

        //Act
        await GgufConversion.ConvertAsync(ConvertFixtureFiles.CheckpointPath("tinyllamabias-124k"), destination,
            null, TestContext.Current.CancellationToken);
        GgufMetadata metadata = await ReadAsync(destination.ToArray());

        //Assert
        metadata.Tensors.Where(tensor => tensor.Shape.Count == 1)
            .All(tensor => tensor.Type == GgufTensorType.F32).Should().BeTrue();
        metadata.Tensors.Any(tensor => tensor.Name == "blk.0.attn_q.bias").Should().BeTrue();
    }

    [Fact]
    public async Task ConvertAsync_pads_the_vocabulary_up_to_the_configured_size()
    {
        //Arrange
        using var destination = new MemoryStream();

        //Act
        await GgufConversion.ConvertAsync(ConvertFixtureFiles.CheckpointPath("tinyllamapad-124k"), destination,
            null, TestContext.Current.CancellationToken);
        GgufMetadata metadata = await ReadAsync(destination.ToArray());

        //Assert
        string[] tokens = metadata.GetExactValue("tokenizer.ggml.tokens").AsStringArray();
        long[] types = metadata.GetExactValue("tokenizer.ggml.token_type").AsInt64Array();
        tokens.Length.Should().Be(328);
        tokens[320].Should().Be("[PAD320]");
        tokens[327].Should().Be("[PAD327]");
        types[327].Should().Be((long)5);
    }

    [Fact]
    public async Task ConvertAsync_refuses_an_architecture_it_does_not_know()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.RewriteConfig("\"LlamaForCausalLM\"", "\"GptBigCodeForCausalLM\"");

        //Act
        Func<Task> act = async () =>
        {
            using var destination = new MemoryStream();
            await GgufConversion.ConvertAsync(checkpoint.DirectoryPath, destination, null,
                TestContext.Current.CancellationToken);
        };

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message
            .Should().Contain("GptBigCodeForCausalLM");
    }

    [Fact]
    public async Task ConvertAsync_refuses_a_tokenizer_kind_it_does_not_know()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.WriteText("tokenizer.json", "{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true}}");

        //Act
        Func<Task> act = async () =>
        {
            using var destination = new MemoryStream();
            await GgufConversion.ConvertAsync(checkpoint.DirectoryPath, destination, null,
                TestContext.Current.CancellationToken);
        };

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message
            .Should().Contain("tokenizer.json");
    }

    [Fact]
    public async Task ConvertAsync_reads_a_sentencepiece_checkpoint_rather_than_the_byte_level_file_set()
    {
        //Act
        byte[] produced = await ConvertAsync(ConvertFixtureFiles.SentencePieceVariant, GgufOutputType.Auto);
        GgufMetadata metadata = await ReadAsync(produced);

        //Assert
        metadata.GetExactValue("tokenizer.ggml.model").AsString().Should().Be("llama");
        metadata.GetExactValue("tokenizer.ggml.pre").AsString().Should().Be("default");
        metadata.GetExactValue("tokenizer.ggml.scores").AsDoubleArray().Should().HaveCount(392);
        metadata.GetExactValue("tokenizer.ggml.merges").Should().BeNull();
    }

    [Fact]
    public async Task ConvertAsync_reads_a_checkpoint_whose_weights_are_split_over_several_files()
    {
        //Arrange
        (string safetensors, string pickle) = ConvertFixtureFiles.ShardedPair;

        //Act
        ConvertResult split = await ConvertResultAsync(safetensors);
        ConvertResult splitPickle = await ConvertResultAsync(pickle);

        //Assert
        split.TensorCount.Should().Be(21);
        splitPickle.TensorCount.Should().Be(21);
        split.SourceBytes.Should().Be(ShardBytes(safetensors, "*.safetensors"));
        splitPickle.SourceBytes.Should().Be(ShardBytes(pickle, "*.bin"));
    }

    [Fact]
    public async Task ConvertAsync_visits_a_sharded_checkpoint_one_shard_at_a_time()
    {
        //Arrange
        (string safetensors, _) = ConvertFixtureFiles.ShardedPair;

        //Act
        byte[] produced = await ConvertAsync(safetensors, GgufOutputType.Auto);
        GgufMetadata metadata = await ReadAsync(produced);
        List<string> names = metadata.Tensors.Select(tensor => tensor.Name).ToList();

        //Assert
        //The second block is in the first shard and the first block in the last, so a reader that sorted the
        //whole set by name or ignored the index's shards would put these the other way round.
        names[0].Should().Be("blk.1.attn_norm.weight");
        names[9].Should().Be("output.weight");
        names[12].Should().Be("blk.0.attn_norm.weight");
        metadata.GetUInt64("block_count").Should().Be(2ul);
    }

    [Fact]
    public async Task ConvertAsync_with_no_index_walks_the_shards_the_folder_holds()
    {
        //Arrange
        (string safetensors, _) = ConvertFixtureFiles.ShardedPair;
        byte[] withIndex = await ConvertAsync(safetensors, GgufOutputType.Auto);
        using var checkpoint = new TempCheckpointDirectory(safetensors);
        checkpoint.DeleteFile("model.safetensors.index.json");

        //Act
        using var destination = new MemoryStream();
        await GgufConversion.ConvertAsync(checkpoint.DirectoryPath, destination,
            new ConvertOptions { ModelId = safetensors }, TestContext.Current.CancellationToken);

        //Assert
        destination.ToArray().SequenceEqual(withIndex).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertAsync_refuses_a_rotary_embedding_it_cannot_reproduce()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.RewriteConfig("\"rope_scaling\": null",
            "\"rope_scaling\": {\"rope_type\": \"llama3\", \"factor\": 8.0}");

        //Act
        Func<Task> act = async () =>
        {
            using var destination = new MemoryStream();
            await GgufConversion.ConvertAsync(checkpoint.DirectoryPath, destination, null,
                TestContext.Current.CancellationToken);
        };

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("llama3");
    }

    [Fact]
    public async Task ConvertAsync_refuses_a_folder_with_no_weights()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.DeleteFile("model.safetensors");

        //Act
        Func<Task> act = async () =>
        {
            using var destination = new MemoryStream();
            await GgufConversion.ConvertAsync(checkpoint.DirectoryPath, destination, null,
                TestContext.Current.CancellationToken);
        };

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("no weights to convert");
    }

    [Fact]
    public async Task ConvertAsync_takes_the_general_metadata_from_the_model_id_it_is_given()
    {
        //Arrange
        var options = new ConvertOptions { ModelId = "an-organization/Tinyllama-123k" };
        using var destination = new MemoryStream();

        //Act
        await GgufConversion.ConvertAsync(ConvertFixtureFiles.CheckpointPath("tinyllamabin-123k"), destination,
            options, TestContext.Current.CancellationToken);
        GgufMetadata metadata = await ReadAsync(destination.ToArray());

        //Assert
        metadata.GetExactValue("general.name").AsString().Should().Be("Tinyllama 123k");
        metadata.GetExactValue("general.organization").AsString().Should().Be("An Organization");
        metadata.GetExactValue("general.basename").AsString().Should().Be("Tinyllama");
    }

    [Fact]
    public async Task ConvertAsync_writes_a_special_token_the_files_do_not_declare_as_an_ordinary_token()
    {
        //Act
        byte[] produced = await ConvertAsync(
            ConvertFixtureFiles.UndeclaredSpecialsVariant, GgufOutputType.Auto);
        GgufMetadata metadata = await ReadAsync(produced);

        //Assert
        TokenTypes(metadata).Take(4).Should().BeEquivalentTo(new[] { 1, 1, 1, 1 });
    }

    [Fact]
    public async Task ConvertAsync_with_supplied_special_tokens_writes_them_as_control_tokens()
    {
        //Act
        byte[] produced = await ConvertAsync(
            ConvertFixtureFiles.UndeclaredSpecialsVariant,
            GgufOutputType.Auto,
            ConvertFixtureFiles.UndeclaredSpecialTokens);
        GgufMetadata metadata = await ReadAsync(produced);

        //Assert
        TokenTypes(metadata).Take(4).Should().BeEquivalentTo(new[] { 3, 3, 3, 3 });
        TokenTypes(metadata)[4].Should().Be((int)GgufTokenType.Normal);
    }

    [Fact]
    public async Task ConvertAsync_with_supplied_special_tokens_changes_nothing_but_those_token_types()
    {
        //Arrange
        byte[] oracle = await File.ReadAllBytesAsync(
            ConvertFixtureFiles.OraclePath(ConvertFixtureFiles.UndeclaredSpecialsVariant, "auto"),
            TestContext.Current.CancellationToken);

        //Act
        byte[] produced = await ConvertAsync(
            ConvertFixtureFiles.UndeclaredSpecialsVariant,
            GgufOutputType.Auto,
            ConvertFixtureFiles.UndeclaredSpecialTokens);

        //Assert
        produced.Length.Should().Be(oracle.Length);
        DifferingOffsets(produced, oracle).Should().HaveCount(4);
    }

    [Fact]
    public async Task ConvertAsync_with_a_supplied_token_that_is_not_in_the_vocabulary_names_it()
    {
        //Arrange
        Func<Task> act = () => ConvertAsync(
            ConvertFixtureFiles.UndeclaredSpecialsVariant, GgufOutputType.Auto, new[] { "<pad>", "<nowhere>" });

        //Act and assert
        ArgumentException thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        thrown.Message.Should().Contain("<nowhere>");
        thrown.ParamName.Should().Be("options");
    }

    [Fact]
    public async Task ConvertAsync_with_an_empty_supplied_token_names_its_position()
    {
        //Arrange
        Func<Task> act = () => ConvertAsync(
            ConvertFixtureFiles.UndeclaredSpecialsVariant, GgufOutputType.Auto, new[] { "<pad>", string.Empty });

        //Act and assert
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("position 1");
    }

    [Fact]
    public async Task ConvertAsync_with_supplied_special_tokens_leaves_a_declared_vocabulary_alone()
    {
        //Arrange
        byte[] oracle = await File.ReadAllBytesAsync(ConvertFixtureFiles.OraclePath("tinyllama-123k", "auto"),
            TestContext.Current.CancellationToken);

        //Act
        byte[] produced = await ConvertAsync(
            "tinyllama-123k", GgufOutputType.Auto, ConvertFixtureFiles.UndeclaredSpecialTokens);

        //Assert
        produced.SequenceEqual(oracle).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertAsync_reports_the_phases_it_is_about_to_run()
    {
        //Arrange
        var progress = new RecordingProgress();
        using var scratch = new TempScratchDirectory();

        //Act
        await GgufConversion.ConvertAsync(ConvertFixtureFiles.CheckpointPath("tinyllama-123k"),
            scratch.Combine("model.gguf"), null, progress, TestContext.Current.CancellationToken);

        //Assert
        progress.Statuses.Should().Equal("reading checkpoint", "writing gguf");
    }

    /// <summary>Converts one fixture and reports what the conversion said about it.</summary>
    private static async Task<ConvertResult> ConvertResultAsync(string variant)
    {
        using var destination = new MemoryStream();
        return await GgufConversion.ConvertAsync(ConvertFixtureFiles.CheckpointPath(variant), destination, null,
            TestContext.Current.CancellationToken);
    }

    /// <summary>The size of every weight file of one fixture added together.</summary>
    private static long ShardBytes(string variant, string pattern)
    {
        long total = 0;
        foreach (string path in Directory.GetFiles(ConvertFixtureFiles.CheckpointPath(variant), pattern))
        {
            total += new FileInfo(path).Length;
        }

        return total;
    }

    private static async Task<byte[]> ConvertAsync(string variant, GgufOutputType outputType,
        IReadOnlyList<string> addedSpecialTokens = null)
    {
        var options = new ConvertOptions
        {
            OutputType = outputType,
            AddedSpecialTokens = addedSpecialTokens
        };
        using var destination = new MemoryStream();
        await GgufConversion.ConvertAsync(ConvertFixtureFiles.CheckpointPath(variant), destination, options,
            TestContext.Current.CancellationToken);
        return destination.ToArray();
    }

    /// <summary>The token types a converted file carries, as ordinary numbers.</summary>
    private static IReadOnlyList<int> TokenTypes(GgufMetadata metadata)
    {
        long[] values = metadata.GetExactValue("tokenizer.ggml.token_type").AsInt64Array();
        var types = new List<int>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            types.Add((int)values[i]);
        }

        return types;
    }

    /// <summary>The offsets at which two files of the same length differ.</summary>
    private static List<int> DifferingOffsets(byte[] produced, byte[] oracle)
    {
        var offsets = new List<int>();
        for (int i = 0; i < produced.Length && i < oracle.Length; i++)
        {
            if (produced[i] != oracle[i])
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }

    private static async Task<GgufMetadata> ReadAsync(byte[] content)
    {
        using var stream = new MemoryStream(content);
        return await GgufMetadata.ReadAsync(stream, null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Names every difference between two GGUF files, so that a failing comparison reports the key or the tensor
    /// that drifted rather than an offset.
    /// </summary>
    private static List<string> Describe(byte[] produced, byte[] oracle)
    {
        var differences = new List<string>();
        GgufMetadata ours;
        GgufMetadata theirs;
        try
        {
            ours = ReadAsync(produced).GetAwaiter().GetResult();
            theirs = ReadAsync(oracle).GetAwaiter().GetResult();
        }
        catch (ModelManagerException exception)
        {
            differences.Add("the produced file could not be read back: " + exception.Message);
            return differences;
        }

        if (!ours.Keys.SequenceEqual(theirs.Keys, StringComparer.Ordinal))
        {
            differences.Add("keys: ours [" + string.Join(", ", ours.Keys) + "] against theirs ["
                + string.Join(", ", theirs.Keys) + "]");
        }

        foreach (string key in theirs.Keys.Intersect(ours.Keys, StringComparer.Ordinal))
        {
            GgufValue ourValue = ours.GetExactValue(key);
            GgufValue theirValue = theirs.GetExactValue(key);
            if (ourValue.Type != theirValue.Type || ourValue.ArrayElementType != theirValue.ArrayElementType)
            {
                differences.Add(key + ": type " + ourValue.Type + "/" + ourValue.ArrayElementType
                    + " against " + theirValue.Type + "/" + theirValue.ArrayElementType);
                continue;
            }

            if (!DescribeValue(ourValue).Equals(DescribeValue(theirValue), StringComparison.Ordinal))
            {
                differences.Add(key + ": " + Shorten(DescribeValue(ourValue)) + " against "
                    + Shorten(DescribeValue(theirValue)));
            }
        }

        if (!ours.Tensors.Select(tensor => tensor.Name)
                .SequenceEqual(theirs.Tensors.Select(tensor => tensor.Name), StringComparer.Ordinal))
        {
            differences.Add("tensor names or their order differ");
        }

        foreach (GgufTensorInfo theirTensor in theirs.Tensors)
        {
            GgufTensorInfo ourTensor = ours.Tensors.FirstOrDefault(
                tensor => string.Equals(tensor.Name, theirTensor.Name, StringComparison.Ordinal));
            if (ourTensor == null)
            {
                differences.Add(theirTensor.Name + ": missing");
                continue;
            }

            if (ourTensor.Type != theirTensor.Type)
            {
                differences.Add(theirTensor.Name + ": type " + ourTensor.TypeName + " against "
                    + theirTensor.TypeName);
            }

            if (!ourTensor.Shape.SequenceEqual(theirTensor.Shape))
            {
                differences.Add(theirTensor.Name + ": shape [" + string.Join(", ", ourTensor.Shape)
                    + "] against [" + string.Join(", ", theirTensor.Shape) + "]");
            }
            else if (ourTensor.Offset != theirTensor.Offset)
            {
                differences.Add(theirTensor.Name + ": offset " + ourTensor.Offset + " against "
                    + theirTensor.Offset);
            }
            else if (!SameBytes(produced, ours, ourTensor, oracle, theirs, theirTensor))
            {
                differences.Add(theirTensor.Name + ": the values differ");
            }
        }

        return differences;
    }

    private static bool SameBytes(byte[] produced, GgufMetadata ours, GgufTensorInfo ourTensor, byte[] oracle,
        GgufMetadata theirs, GgufTensorInfo theirTensor)
    {
        long ourStart = ours.TensorDataOffset + (long)ourTensor.Offset;
        long theirStart = theirs.TensorDataOffset + (long)theirTensor.Offset;
        if (ourStart + ourTensor.ByteCount > produced.Length
            || theirStart + theirTensor.ByteCount > oracle.Length)
        {
            return false;
        }

        return new ReadOnlySpan<byte>(produced, (int)ourStart, (int)ourTensor.ByteCount)
            .SequenceEqual(new ReadOnlySpan<byte>(oracle, (int)theirStart, (int)theirTensor.ByteCount));
    }

    private static string DescribeValue(GgufValue value)
    {
        if (!value.IsArray)
        {
            return value.ToString();
        }

        string[] strings = value.AsStringArray();
        if (strings != null)
        {
            return string.Join("", strings);
        }

        long[] integers = value.AsInt64Array();
        if (integers != null)
        {
            return string.Join(",", integers);
        }

        ulong[] unsigned = value.AsUInt64Array();
        if (unsigned != null)
        {
            return string.Join(",", unsigned);
        }

        double[] reals = value.AsDoubleArray();
        return reals != null ? string.Join(",", reals) : value.ToString();
    }

    private static string Shorten(string value)
    {
        return value.Length <= 160 ? value : value.Substring(0, 160) + "...";
    }
}
