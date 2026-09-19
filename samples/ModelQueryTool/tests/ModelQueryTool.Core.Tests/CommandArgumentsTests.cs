using ModelQueryTool.Services.Commands;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>Tests for <see cref="CommandArguments"/>, which is how every command reads what was typed.</summary>
public class CommandArgumentsTests
{
    /// <summary>The confirmation on its own confirms and leaves nothing behind.</summary>
    [Fact]
    public void TakeConfirmation_takes_the_flag_on_its_own()
    {
        //Act
        var confirmed = CommandArguments.TakeConfirmation("-y", out var remainder);

        //Assert
        confirmed.Should().Be(true);
        remainder.Should().Be(string.Empty);
    }

    /// <summary>The confirmation in front of an argument leaves the argument exactly as it was typed.</summary>
    [Fact]
    public void TakeConfirmation_leaves_the_argument_verbatim()
    {
        //Act
        var confirmed = CommandArguments.TakeConfirmation("-y   \"be  brief\"", out var remainder);

        //Assert
        confirmed.Should().Be(true);
        remainder.Should().Be("\"be  brief\"");
    }

    /// <summary>A word that merely starts with the flag's letters is not a confirmation.</summary>
    [Fact]
    public void TakeConfirmation_does_not_take_a_longer_word()
    {
        //Act
        var confirmed = CommandArguments.TakeConfirmation("-yes please", out var remainder);

        //Assert
        confirmed.Should().Be(false);
        remainder.Should().Be("-yes please");
    }

    /// <summary>The flag after the argument is not a confirmation: it is accepted before it and nowhere else.</summary>
    [Fact]
    public void TakeConfirmation_does_not_take_the_flag_after_the_argument()
    {
        //Act
        var confirmed = CommandArguments.TakeConfirmation("\"be brief\" -y", out var remainder);

        //Assert
        confirmed.Should().Be(false);
        remainder.Should().Be("\"be brief\" -y");
    }

    /// <summary>Nothing typed is no confirmation and no argument.</summary>
    [Fact]
    public void TakeConfirmation_copes_with_nothing_at_all()
    {
        //Act
        var confirmed = CommandArguments.TakeConfirmation(null, out var remainder);

        //Assert
        confirmed.Should().Be(false);
        remainder.Should().Be(string.Empty);
    }

    /// <summary>One pair of quotes comes off; the quotes inside the text stay where the user put them.</summary>
    [Fact]
    public void StripOuterQuotes_keeps_the_quotes_inside_the_text() =>
        CommandArguments.StripOuterQuotes("\"answer like a \"friendly\" tutor\"")
            .Should().Be("answer like a \"friendly\" tutor");

    /// <summary>Text with no quotes around it is left alone.</summary>
    [Fact]
    public void StripOuterQuotes_leaves_unquoted_text_alone() =>
        CommandArguments.StripOuterQuotes("be brief").Should().Be("be brief");

    /// <summary>A pair of quotes with nothing between them is how a system prompt is cleared.</summary>
    [Fact]
    public void StripOuterQuotes_turns_an_empty_pair_into_nothing() =>
        CommandArguments.StripOuterQuotes("\"\"").Should().Be(string.Empty);
}
