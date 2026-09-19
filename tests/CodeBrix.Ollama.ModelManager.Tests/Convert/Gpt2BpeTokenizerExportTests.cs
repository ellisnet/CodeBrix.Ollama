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
/// Covers the GPT-2 byte-level BPE export: the vocabulary and its token types, the merge table's first-line
/// rule, where the special-token identifiers come from, and what it refuses.
/// </summary>
public sealed class Gpt2BpeTokenizerExportTests
{
    [Fact]
    public async Task LoadAsync_reads_the_fixture_vocabulary()
    {
        //Arrange
        string directory = ConvertFixtureFiles.CheckpointPath("tinyllama-123k");

        //Act
        TokenizerExport export = await LoadAsync(directory);

        //Assert
        export.Model.Should().Be("gpt2");
        export.Pre.Should().Be("gpt-2");
        export.Tokens.Should().HaveCount(320);
        export.Merges.Should().HaveCount(60);
        export.Tokens[0].Should().Be("<pad>");
        export.TokenTypes.Take(4).Should().BeEquivalentTo(new[] { 3, 3, 3, 3 });
        export.TokenTypes[4].Should().Be((int)GgufTokenType.Normal);
    }

    [Fact]
    public async Task LoadAsync_takes_the_special_identifiers_from_the_model_configuration()
    {
        //Arrange
        string directory = ConvertFixtureFiles.CheckpointPath("tinyllamagqa-115k");

        //Act
        TokenizerExport export = await LoadAsync(directory);

        //Assert
        export.SpecialTokenIds.Keys.Should().BeEquivalentTo(new[] { "bos", "eos", "unk", "pad" });
        export.SpecialTokenIds["bos"].Should().Be(2L);
        export.SpecialTokenIds["eos"].Should().Be(3L);
        export.SpecialTokenIds["unk"].Should().Be(1L);
        export.SpecialTokenIds["pad"].Should().Be(0L);
        export.AddSpecialTokens["bos"].Should().BeFalse();
        export.ChatTemplate.Should().NotBeNullOrEmpty();
        export.AddSpacePrefix.Should().Be(false);
    }

    [Fact]
    public async Task LoadAsync_writes_no_identifier_for_a_kind_the_configuration_does_not_name()
    {
        //Arrange
        string directory = ConvertFixtureFiles.CheckpointPath("tinyllama-123k");

        //Act
        TokenizerExport export = await LoadAsync(directory);

        //Assert
        export.SpecialTokenIds.ContainsKey("unk").Should().BeFalse();
        export.AddSpecialTokens.Should().BeEmpty();
        export.ChatTemplate.Should().BeNull();
        export.AddSpacePrefix.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_pads_the_vocabulary_up_to_the_configured_size()
    {
        //Arrange
        string directory = ConvertFixtureFiles.CheckpointPath("tinyllamapad-124k");

        //Act
        TokenizerExport export = await LoadAsync(directory);

        //Assert
        export.Tokens.Should().HaveCount(328);
        export.Tokens[323].Should().Be("[PAD323]");
        export.TokenTypes[323].Should().Be((int)GgufTokenType.Unused);
    }

    [Fact]
    public async Task LoadAsync_refuses_a_checkpoint_whose_tokenizer_is_a_sentencepiece_model()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.WriteFile("tokenizer.model", new byte[] { 0x0a });

        //Act
        Func<Task> act = async () => await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message
            .Should().Contain("SentencePiece");
    }

    [Fact]
    public async Task LoadAsync_refuses_a_checkpoint_with_no_merges()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.DeleteFile("merges.txt");

        //Act
        Func<Task> act = async () => await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message
            .Should().Contain("no merges.txt");
    }

    [Fact]
    public async Task LoadAsync_refuses_a_vocabulary_that_runs_past_the_configured_size()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.RewriteConfig("\"vocab_size\": 320", "\"vocab_size\": 64");

        //Act
        Func<Task> act = async () => await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("outside the vocabulary size");
    }

    [Theory]
    [InlineData("<pad>", true)]
    [InlineData("<|endoftext|>", true)]
    [InlineData("<unused42>", true)]
    [InlineData("<bos>", false)]
    [InlineData("hello", false)]
    public void DoesTokenLookSpecial_recognises_the_shapes_the_engine_recognises(string token, bool expected)
        => Gpt2BpeTokenizerExport.DoesTokenLookSpecial(token).Should().Be(expected);

    [Fact]
    public async Task LoadAsync_keeps_every_merge_of_a_file_with_no_header()
    {
        //Arrange
        string path = Path.Combine(ConvertFixtureFiles.CheckpointPath("tinyllama-123k"), "merges.txt");
        string[] lines = await File.ReadAllLinesAsync(path, Encoding.UTF8,
            TestContext.Current.CancellationToken);

        //Act
        TokenizerExport export = await LoadAsync(ConvertFixtureFiles.CheckpointPath("tinyllama-123k"));

        //Assert
        export.Merges.Should().HaveCount(lines.Count(line => line.Trim().Length > 0));
    }

    private static async Task<TokenizerExport> LoadAsync(string directory,
        IReadOnlyList<string> addedSpecialTokens = null)
    {
        HuggingFaceConfig config = await HuggingFaceConfig.LoadAsync(directory,
            TestContext.Current.CancellationToken);
        TokenizerConfig tokenizerConfig = await TokenizerConfig.LoadAsync(directory,
            TestContext.Current.CancellationToken);
        return await Gpt2BpeTokenizerExport.LoadAsync(directory, config, tokenizerConfig, addedSpecialTokens,
            TestContext.Current.CancellationToken);
    }
}
