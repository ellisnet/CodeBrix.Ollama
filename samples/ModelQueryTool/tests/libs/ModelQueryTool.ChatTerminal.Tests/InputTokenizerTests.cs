using ModelQueryTool.ChatTerminal.Editing;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="InputTokenizer"/>.</summary>
public class InputTokenizerTests
{
    [Fact]
    public void printable_characters_become_character_tokens()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var tokens = tokenizer.Feed("ab");

        //Assert
        tokens.Should().HaveCount(2);
        tokens[0].Kind.Should().Be(InputTokenKind.Character);
        tokens[0].Character.Should().Be('a');
        tokens[1].Character.Should().Be('b');
    }

    [Fact]
    public void control_characters_become_control_tokens()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var tokens = tokenizer.Feed("\r\x03\x7f");

        //Assert
        tokens.Should().HaveCount(3);
        tokens[0].Kind.Should().Be(InputTokenKind.Control);
        tokens[0].Character.Should().Be('\r');
        tokens[1].Character.Should().Be('\x03');
        tokens[2].Character.Should().Be('\x7f');
    }

    [Fact]
    public void csi_arrow_sequences_decode_to_edit_keys()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var tokens = tokenizer.Feed("\x1b[A\x1b[B\x1b[C\x1b[D");

        //Assert
        tokens.Should().HaveCount(4);
        tokens[0].Key.Should().Be(EditKey.Up);
        tokens[1].Key.Should().Be(EditKey.Down);
        tokens[2].Key.Should().Be(EditKey.Right);
        tokens[3].Key.Should().Be(EditKey.Left);
    }

    [Fact]
    public void ss3_sequences_decode_to_edit_keys()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var tokens = tokenizer.Feed("\x1bOA\x1bOF");

        //Assert
        tokens.Should().HaveCount(2);
        tokens[0].Key.Should().Be(EditKey.Up);
        tokens[1].Key.Should().Be(EditKey.End);
    }

    [Fact]
    public void tilde_sequences_decode_home_delete_end_and_paging()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var tokens = tokenizer.Feed("\x1b[1~\x1b[3~\x1b[4~\x1b[5~\x1b[6~");

        //Assert
        tokens.Should().HaveCount(5);
        tokens[0].Key.Should().Be(EditKey.Home);
        tokens[1].Key.Should().Be(EditKey.Delete);
        tokens[2].Key.Should().Be(EditKey.End);
        tokens[3].Key.Should().Be(EditKey.PageUp);
        tokens[4].Key.Should().Be(EditKey.PageDown);
    }

    [Fact]
    public void an_escape_sequence_split_across_feeds_still_decodes()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var first = tokenizer.Feed("\x1b");
        var second = tokenizer.Feed("[");
        var third = tokenizer.Feed("A");

        //Assert
        first.Should().BeEmpty();
        second.Should().BeEmpty();
        third.Should().HaveCount(1);
        third[0].Key.Should().Be(EditKey.Up);
    }

    [Fact]
    public void alt_prefixed_character_drops_the_escape()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var tokens = tokenizer.Feed("\x1bx");

        //Assert
        tokens.Should().HaveCount(1);
        tokens[0].Kind.Should().Be(InputTokenKind.Character);
        tokens[0].Character.Should().Be('x');
    }

    [Fact]
    public void unknown_csi_sequences_are_swallowed()
    {
        //Arrange
        var tokenizer = new InputTokenizer();

        //Act
        var tokens = tokenizer.Feed("\x1b[?25hZ");

        //Assert
        tokens.Should().HaveCount(1);
        tokens[0].Character.Should().Be('Z');
    }
}
