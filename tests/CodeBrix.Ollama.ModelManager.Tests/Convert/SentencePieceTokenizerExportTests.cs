using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the SentencePiece export: the six token types and where each comes from, the scores, the padding of a
/// vocabulary shorter than the configured size, the two added-token files and the order they are applied in,
/// and the keys this road writes that the byte-level one does not.
/// </summary>
public sealed class SentencePieceTokenizerExportTests
{
    [Fact]
    public async Task LoadAsync_writes_the_llama_tokenizer_model_and_the_default_pre_tokenizer()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.Model.Should().Be("llama");
        export.Pre.Should().Be("default");
        export.Merges.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_pads_the_vocabulary_up_to_the_configured_size()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.Tokens.Should().HaveCount(392);
        export.Scores.Should().HaveCount(392);
        export.TokenTypes.Should().HaveCount(392);
        export.Tokens[388].Should().Be("[PAD388]");
        export.TokenTypes[388].Should().Be((int)GgufTokenType.Unused);
        export.Scores[388].Should().Be(SentencePieceTokenizerExport.PlaceholderScore);
    }

    [Fact]
    public async Task LoadAsync_gives_every_kind_of_piece_the_type_the_engine_gives_it()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.TokenTypes[1].Should().Be((int)GgufTokenType.Unknown);
        export.TokenTypes[4].Should().Be((int)GgufTokenType.Control);
        export.TokenTypes[8].Should().Be((int)GgufTokenType.Byte);
        export.TokenTypes[264].Should().Be((int)GgufTokenType.Normal);

        //A piece the MODEL calls user-defined is written as an ordinary token; only an added-token file makes
        //one user-defined. Identifier 7 is the second user-defined symbol the model was trained with and no
        //file names it, so it is normal.
        export.TokenTypes[7].Should().Be((int)GgufTokenType.Normal);
    }

    [Fact]
    public async Task LoadAsync_keeps_the_score_of_every_trained_piece()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.Scores[264].Should().Be(-0f);
        BitConverter.SingleToInt32Bits(export.Scores[264]).Should().Be(int.MinValue);
        export.Scores[265].Should().Be(-1f);
    }

    [Fact]
    public async Task LoadAsync_makes_a_token_of_added_tokens_json_user_defined()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.Tokens[384].Should().Be("<extra0>");
        export.TokenTypes[384].Should().Be((int)GgufTokenType.UserDefined);
        export.Scores[384].Should().Be(SentencePieceTokenizerExport.AddedTokenScore);
    }

    [Fact]
    public async Task LoadAsync_turns_the_space_marker_of_an_added_token_into_a_space()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.Tokens[322].Should().Be(" gap");
        export.TokenTypes[322].Should().Be((int)GgufTokenType.UserDefined);
    }

    [Fact]
    public async Task LoadAsync_makes_an_added_token_that_only_looks_special_a_control_token()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.Tokens[323].Should().Be("<|looksspecial|>");
        export.TokenTypes[323].Should().Be((int)GgufTokenType.Control);
    }

    [Fact]
    public async Task LoadAsync_reads_the_added_token_table_after_the_added_tokens_file()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Variant);
        checkpoint.WriteText("added_tokens.json", "{\"<fromfile>\": 386}");

        //Act
        TokenizerExport export = await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        //Both files name identifier 386; the table is applied second, so the table's answer is the one written.
        export.Tokens[386].Should().Be("<extra2>");
    }

    [Fact]
    public async Task LoadAsync_ignores_an_added_token_outside_the_vocabulary()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Variant);
        checkpoint.WriteText("added_tokens.json", "{\"<beyond>\": 4096}");

        //Act
        TokenizerExport export = await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        export.Tokens.Should().HaveCount(392);
        export.Tokens.Should().NotContain("<beyond>");
    }

    [Fact]
    public async Task LoadAsync_writes_no_identifier_for_a_special_token_outside_the_vocabulary()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Variant);
        checkpoint.RewriteConfig("\"eos_token_id\": 3", "\"eos_token_id\": 9999");

        //Act
        TokenizerExport export = await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        export.SpecialTokenIds.ContainsKey("eos").Should().BeFalse();
        export.SpecialTokenIds["bos"].Should().Be(2L);
    }

    [Fact]
    public async Task LoadAsync_carries_the_flags_and_the_chat_template_of_the_tokenizer_configuration()
    {
        //Act
        TokenizerExport export = await LoadAsync(Fixture);

        //Assert
        export.AddSpacePrefix.Should().Be(true);
        export.AddSpecialTokens["bos"].Should().BeTrue();
        export.AddSpecialTokens["eos"].Should().BeFalse();
        export.ChatTemplate.Should().NotBeNullOrEmpty();
        export.SpecialTokenIds["unk"].Should().Be(1L);
    }

    [Fact]
    public async Task LoadAsync_falls_back_to_the_models_own_count_when_the_configuration_names_no_size()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Variant);
        checkpoint.RewriteConfig("\"vocab_size\": 392", "\"vocab_size\": 0");

        //Act
        TokenizerExport export = await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        export.Tokens.Should().HaveCount(384);
    }

    [Fact]
    public async Task LoadAsync_refuses_an_added_tokens_file_that_is_not_a_map_of_identifiers()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory(Variant);
        checkpoint.WriteText("added_tokens.json", "{\"<broken>\": \"not an identifier\"}");

        //Act
        Func<Task> act = async () => await LoadAsync(checkpoint.DirectoryPath);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("<broken>");
    }

    private static string Variant => ConvertFixtureFiles.SentencePieceVariant;

    private static string Fixture => ConvertFixtureFiles.CheckpointPath(Variant);

    private static async Task<TokenizerExport> LoadAsync(string directory)
    {
        HuggingFaceConfig config = await HuggingFaceConfig.LoadAsync(directory,
            TestContext.Current.CancellationToken);
        TokenizerConfig tokenizerConfig = await TokenizerConfig.LoadAsync(directory,
            TestContext.Current.CancellationToken);
        return await SentencePieceTokenizerExport.LoadAsync(directory, config, tokenizerConfig,
            TestContext.Current.CancellationToken);
    }
}
