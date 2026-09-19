using System;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers reading a <c>tokenizer.model</c> with the library's own Protocol Buffers codec: the three fields of a
/// piece, the defaults the schema gives the two that may be absent, the fields the reader skips, and what it
/// refuses.
/// </summary>
public sealed class SentencePieceModelTests
{
    [Fact]
    public async Task LoadAsync_reads_the_fixture_vocabulary()
    {
        //Act
        SentencePieceModel model = await SentencePieceModel.LoadAsync(
            ConvertFixtureFiles.CheckpointPath(ConvertFixtureFiles.SentencePieceVariant),
            TestContext.Current.CancellationToken);

        //Assert
        model.Pieces.Should().HaveCount(384);
        model.Pieces[0].Text.Should().Be("<pad>");
        model.Pieces[0].Type.Should().Be(SentencePieceTokenType.Control);
        model.Pieces[1].Type.Should().Be(SentencePieceTokenType.Unknown);
        model.Pieces[6].Type.Should().Be(SentencePieceTokenType.UserDefined);
        model.Pieces[8].Text.Should().Be("<0x00>");
        model.Pieces[8].Type.Should().Be(SentencePieceTokenType.Byte);
        model.Pieces[264].Type.Should().Be(SentencePieceTokenType.Normal);
    }

    [Fact]
    public void Parse_reads_a_piece_text_score_and_kind()
    {
        //Arrange
        byte[] content = new SentencePieceModelBuilder()
            .Piece("<unk>", 0f, 2)
            .Piece("▁the", -1.5f, 1)
            .ToArray();

        //Act
        SentencePieceModel model = SentencePieceModel.Parse(content, "tokenizer.model");

        //Assert
        model.Pieces.Should().HaveCount(2);
        model.Pieces[1].Text.Should().Be("▁the");
        model.Pieces[1].Score.Should().Be(-1.5f);
        model.Pieces[1].Type.Should().Be(SentencePieceTokenType.Normal);
    }

    [Fact]
    public void Parse_gives_a_piece_that_names_neither_score_nor_kind_the_schema_defaults()
    {
        //Arrange
        byte[] content = new SentencePieceModelBuilder().BarePiece("plain").ToArray();

        //Act
        SentencePieceModel model = SentencePieceModel.Parse(content, "tokenizer.model");

        //Assert
        model.Pieces[0].Score.Should().Be(0f);
        model.Pieces[0].Type.Should().Be(SentencePieceTokenType.Normal);
    }

    [Fact]
    public void Parse_keeps_the_sign_of_a_negative_zero_score()
    {
        //Arrange
        byte[] content = new SentencePieceModelBuilder().Piece("first", -0f, 1).ToArray();

        //Act
        SentencePieceModel model = SentencePieceModel.Parse(content, "tokenizer.model");

        //Assert
        BitConverter.SingleToInt32Bits(model.Pieces[0].Score).Should().Be(int.MinValue);
    }

    [Fact]
    public void Parse_skips_the_fields_it_does_not_model()
    {
        //Arrange
        byte[] content = new SentencePieceModelBuilder()
            .PieceWithUnknownField("kept", -2f)
            .UnknownField(2, "a trainer specification this reader never reads")
            .UnknownField(3, "a normalizer specification")
            .ToArray();

        //Act
        SentencePieceModel model = SentencePieceModel.Parse(content, "tokenizer.model");

        //Assert
        model.Pieces.Should().HaveCount(1);
        model.Pieces[0].Text.Should().Be("kept");
        model.Pieces[0].Score.Should().Be(-2f);
    }

    [Fact]
    public void Parse_refuses_a_file_that_ends_part_way_through_a_piece()
    {
        //Arrange
        byte[] content = new SentencePieceModelBuilder().Piece("truncated", -1f, 1).ToTruncatedArray();

        //Act
        Action act = () => SentencePieceModel.Parse(content, "tokenizer.model");

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message
            .Should().Contain("tokenizer.model");
    }

    [Fact]
    public void Parse_refuses_a_file_that_holds_no_pieces()
    {
        //Arrange
        byte[] content = new SentencePieceModelBuilder()
            .UnknownField(2, "only a trainer specification").ToArray();

        //Act
        Action act = () => SentencePieceModel.Parse(content, "tokenizer.model");

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message
            .Should().Contain("no pieces");
    }

    [Fact]
    public async Task LoadAsync_refuses_a_directory_with_no_tokenizer_model()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");

        //Act
        Func<Task> act = async () => await SentencePieceModel.LoadAsync(
            checkpoint.DirectoryPath, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>()).Which.Message
            .Should().Contain("is not there");
    }

    //The two enumerations are internal to the library, so the cases are written as the numbers the file format
    //and the GGUF specification give them; an InlineData argument of an internal type cannot be public.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(4, 1)]
    [InlineData(99, 1)]
    public void ToGgufTokenType_maps_a_piece_kind_the_way_the_engine_maps_it(int piece, int expected)
        => ((int)SentencePieceTokenizerExport.ToGgufTokenType((SentencePieceTokenType)piece))
            .Should().Be(expected);
}
