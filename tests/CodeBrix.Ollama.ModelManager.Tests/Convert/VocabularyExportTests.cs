using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the choice of tokenizer road: which file decides it, in which order, and what a checkpoint whose
/// tokenizer this version does not read is refused with.
/// </summary>
public sealed class VocabularyExportTests
{
    [Fact]
    public async Task LoadAsync_takes_the_sentencepiece_road_for_a_checkpoint_with_a_tokenizer_model()
    {
        //Act
        TokenizerExport export = await LoadAsync(
            ConvertFixtureFiles.CheckpointPath(ConvertFixtureFiles.SentencePieceVariant));

        //Assert
        export.Model.Should().Be("llama");
        export.Scores.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_takes_the_byte_level_road_for_a_checkpoint_with_vocab_json_and_merges()
    {
        //Act
        TokenizerExport export = await LoadAsync(ConvertFixtureFiles.CheckpointPath("tinyllama-123k"));

        //Assert
        export.Model.Should().Be("gpt2");
        export.Scores.Should().BeEmpty();
        export.Merges.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_prefers_a_tokenizer_model_to_every_other_tokenizer_file()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(ConvertFixtureFiles.SentencePieceVariant);
        checkpoint.WriteText("tokenizer.json", "{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true}}");
        checkpoint.WriteText("vocab.json", "{\"<pad>\":0}");
        checkpoint.WriteText("merges.txt", "a b\n");

        //Act
        TokenizerExport export = await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        export.Model.Should().Be("llama");
    }

    [Fact]
    public async Task LoadAsync_refuses_a_checkpoint_whose_only_tokenizer_is_a_tokenizer_json()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.WriteText("tokenizer.json", "{\"model\":{\"type\":\"BPE\",\"byte_fallback\":true}}");

        //Act
        Func<Task> act = async () => await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message
            .Should().Contain("tokenizer.json");
    }

    [Fact]
    public async Task LoadAsync_refuses_a_checkpoint_whose_tokenizer_is_a_tekken_json()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");
        checkpoint.WriteText("tekken.json", "{}");

        //Act
        Func<Task> act = async () => await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message
            .Should().Contain("tekken.json");
    }

    [Fact]
    public async Task LoadAsync_refuses_supplied_special_tokens_on_the_sentencepiece_road()
    {
        //Arrange
        string directory = ConvertFixtureFiles.CheckpointPath(ConvertFixtureFiles.SentencePieceVariant);

        //Act
        Func<Task> act = async () => await LoadAsync(directory, new[] { "<pad>" });

        //Assert
        ArgumentException thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        thrown.ParamName.Should().Be("options");
        thrown.Message.Should().Contain("added_tokens.json");
    }

    [Fact]
    public async Task LoadAsync_accepts_an_empty_list_of_supplied_tokens_on_the_sentencepiece_road()
    {
        //Act
        TokenizerExport export = await LoadAsync(
            ConvertFixtureFiles.CheckpointPath(ConvertFixtureFiles.SentencePieceVariant),
            Array.Empty<string>());

        //Assert
        export.Model.Should().Be("llama");
    }

    private static async Task<TokenizerExport> LoadAsync(string directory,
        IReadOnlyList<string> addedSpecialTokens = null)
    {
        HuggingFaceConfig config = await HuggingFaceConfig.LoadAsync(directory,
            TestContext.Current.CancellationToken);
        TokenizerConfig tokenizerConfig = await TokenizerConfig.LoadAsync(directory,
            TestContext.Current.CancellationToken);
        return await VocabularyExport.LoadAsync(directory, config, tokenizerConfig, addedSpecialTokens,
            TestContext.Current.CancellationToken);
    }
}
