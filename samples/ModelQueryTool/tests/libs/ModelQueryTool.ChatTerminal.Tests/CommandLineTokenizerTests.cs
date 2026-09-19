using ModelQueryTool.ChatTerminal.Commands;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="CommandLineTokenizer"/>.</summary>
public class CommandLineTokenizerTests
{
    [Fact]
    public void whitespace_separates_tokens()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("set-context-size  16384   -y now");

        //Assert
        tokens.Should().Equal("set-context-size", "16384", "-y", "now");
    }

    [Fact]
    public void empty_and_blank_lines_yield_no_tokens()
    {
        //Assert
        CommandLineTokenizer.Tokenize("").Should().BeEmpty();
        CommandLineTokenizer.Tokenize("   ").Should().BeEmpty();
        CommandLineTokenizer.Tokenize(null).Should().BeEmpty();
    }

    [Fact]
    public void double_quotes_group_whitespace_into_one_token()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("think \"two words\"");

        //Assert
        tokens.Should().Equal("think", "two words");
    }

    [Fact]
    public void quotes_may_join_mid_token()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("a\"b c\"d");

        //Assert
        tokens.Should().Equal("ab cd");
    }

    [Fact]
    public void backslash_escapes_quote_and_backslash_inside_quotes()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("say \"a \\\"quoted\\\" word\\\\\"");

        //Assert
        tokens.Should().Equal("say", "a \"quoted\" word\\");
    }

    [Fact]
    public void backslash_outside_quotes_is_literal()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize(@"open C:\models\notes.txt");

        //Assert
        tokens.Should().Equal("open", @"C:\models\notes.txt");
    }

    [Fact]
    public void an_unterminated_quote_runs_to_end_of_line()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("think \"unfinished name");

        //Assert
        tokens.Should().Equal("think", "unfinished name");
    }

    [Fact]
    public void an_empty_quoted_token_is_preserved()
    {
        //Act
        var tokens = CommandLineTokenizer.Tokenize("echo \"\"");

        //Assert
        tokens.Should().Equal("echo", "");
    }
}
