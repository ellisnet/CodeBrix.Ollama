using ModelQueryTool.ChatTerminal.Editing;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="PasteRule"/> - what counts as the Enter key.</summary>
public class PasteRuleTests
{
    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void a_chunk_that_is_exactly_one_line_ending_is_the_enter_key(string chunk) =>
        PasteRule.IsEnterKey(chunk).Should().BeTrue();

    [Theory]
    [InlineData("\r\r")]
    [InlineData("\n\r")]
    [InlineData("\n\n")]
    [InlineData("text\r")]
    [InlineData("\rtext")]
    [InlineData("first\rsecond\r")]
    public void a_longer_chunk_is_a_paste(string chunk) =>
        PasteRule.IsEnterKey(chunk).Should().BeFalse();

    [Theory]
    [InlineData("a")]
    [InlineData("\x03")]
    [InlineData("\x1b[A")]
    [InlineData("")]
    public void a_chunk_with_no_line_ending_is_not_the_enter_key(string chunk) =>
        PasteRule.IsEnterKey(chunk).Should().BeFalse();

    [Fact]
    public void a_null_chunk_is_not_the_enter_key() =>
        PasteRule.IsEnterKey(null).Should().BeFalse();

    [Fact]
    public void every_kind_of_line_ending_becomes_one_break()
    {
        //Act
        var result = PasteRule.ToLineBreaks("one\rtwo\r\nthree\nfour");

        //Assert
        result.Should().Be("one\ntwo\nthree\nfour");
    }

    [Fact]
    public void a_trailing_line_ending_becomes_a_trailing_break()
    {
        //Act
        var result = PasteRule.ToLineBreaks("one\r\n");

        //Assert
        result.Should().Be("one\n");
    }

    [Fact]
    public void two_line_endings_in_a_row_are_two_breaks()
    {
        //Act
        var result = PasteRule.ToLineBreaks("one\r\n\r\ntwo");

        //Assert
        result.Should().Be("one\n\ntwo");
    }

    [Fact]
    public void text_with_no_line_endings_comes_back_unchanged() =>
        PasteRule.ToLineBreaks("nothing to do here").Should().Be("nothing to do here");

    [Fact]
    public void null_text_becomes_an_empty_string() =>
        PasteRule.ToLineBreaks(null).Should().Be("");
}
