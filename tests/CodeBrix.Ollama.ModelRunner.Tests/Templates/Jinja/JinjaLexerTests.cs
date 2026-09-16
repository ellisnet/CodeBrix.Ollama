using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>Tests for <see cref="JinjaLexer"/>: the token stream and the whitespace rules.</summary>
public sealed class JinjaLexerTests
{
    /// <summary>Plain text becomes a single text token.</summary>
    [Fact]
    public void Tokenize_with_plain_text_returns_one_text_token()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("hello");

        //Assert
        tokens.Should().HaveCount(2);
        tokens[0].Kind.Should().Be(JinjaTokenKind.Text);
        tokens[0].Value.Should().Be("hello");
        tokens[1].Kind.Should().Be(JinjaTokenKind.EndOfFile);
    }

    /// <summary>An output tag is bracketed by the variable delimiters.</summary>
    [Fact]
    public void Tokenize_with_an_output_tag_brackets_the_expression()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("{{ x }}");

        //Assert
        tokens.Should().HaveCount(4);
        tokens[0].Kind.Should().Be(JinjaTokenKind.VariableStart);
        tokens[1].Kind.Should().Be(JinjaTokenKind.Name);
        tokens[1].Value.Should().Be("x");
        tokens[2].Kind.Should().Be(JinjaTokenKind.VariableEnd);
    }

    /// <summary>A statement tag is bracketed by the block delimiters.</summary>
    [Fact]
    public void Tokenize_with_a_statement_tag_brackets_the_statement()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("{% if x %}");

        //Assert
        tokens.Should().HaveCount(5);
        tokens[0].Kind.Should().Be(JinjaTokenKind.BlockStart);
        tokens[1].Value.Should().Be("if");
        tokens[2].Value.Should().Be("x");
        tokens[3].Kind.Should().Be(JinjaTokenKind.BlockEnd);
    }

    /// <summary>A comment produces no tokens at all.</summary>
    [Fact]
    public void Tokenize_with_a_comment_produces_nothing()
    {
        JinjaLexer.Tokenize("{# nothing here #}").Should().HaveCount(1);
    }

    /// <summary>String escapes are decoded.</summary>
    /// <param name="source">The tag source.</param>
    /// <param name="expected">The decoded string.</param>
    [Theory]
    [InlineData("{{ 'a\\nb' }}", "a\nb")]
    [InlineData("{{ 'a\\tb' }}", "a\tb")]
    [InlineData("{{ 'a\\\\b' }}", "a\\b")]
    [InlineData("{{ \"a\\\"b\" }}", "a\"b")]
    [InlineData("{{ '\\u0041' }}", "A")]
    [InlineData("{{ 'line\none' }}", "line\none")]
    public void Tokenize_decodes_string_escapes(string source, string expected)
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize(source);

        //Assert
        tokens[1].Kind.Should().Be(JinjaTokenKind.String);
        tokens[1].Value.Should().Be(expected);
    }

    /// <summary>Integers and floats are told apart and parsed.</summary>
    [Fact]
    public void Tokenize_reads_numbers()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("{{ 42 }}{{ 4.5 }}{{ 1e3 }}");

        //Assert
        tokens[1].Kind.Should().Be(JinjaTokenKind.Integer);
        tokens[1].Number.Should().Be(42L);
        tokens[4].Kind.Should().Be(JinjaTokenKind.Float);
        tokens[4].Number.Should().Be(4.5d);
        tokens[7].Kind.Should().Be(JinjaTokenKind.Float);
        tokens[7].Number.Should().Be(1000d);
    }

    /// <summary>The two-character operators are read as one token.</summary>
    [Fact]
    public void Tokenize_reads_two_character_operators()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("{{ a ** b // c == d != e >= f <= g }}");

        //Assert
        var operators = new List<string>();
        foreach (JinjaToken token in tokens)
        {
            if (token.Kind == JinjaTokenKind.Operator)
            {
                operators.Add(token.Value);
            }
        }

        operators.Should().Equal("**", "//", "==", "!=", ">=", "<=");
    }

    /// <summary>A dictionary literal's braces do not end the tag.</summary>
    [Fact]
    public void Tokenize_with_a_dictionary_literal_finds_the_real_end()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("{{ {'a': 1}}}");

        //Assert
        tokens[tokens.Count - 2].Kind.Should().Be(JinjaTokenKind.VariableEnd);
    }

    /// <summary>A raw block hands its body back as literal text.</summary>
    [Fact]
    public void Tokenize_with_a_raw_block_keeps_the_body_literal()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("{% raw %}{{ x }}{% endraw %}");

        //Assert
        tokens.Should().HaveCount(2);
        tokens[0].Value.Should().Be("{{ x }}");
    }

    /// <summary>The final newline of the source is dropped, and only one of them.</summary>
    /// <param name="source">The raw source.</param>
    /// <param name="expected">The prepared source.</param>
    [Theory]
    [InlineData("a\n", "a")]
    [InlineData("a\n\n", "a\n")]
    [InlineData("a\r\n", "a")]
    [InlineData("a", "a")]
    [InlineData("", "")]
    public void PrepareSource_drops_one_trailing_newline(string source, string expected)
    {
        JinjaLexer.PrepareSource(source).Should().Be(expected);
    }

    /// <summary>An index becomes a one-based line and column.</summary>
    [Fact]
    public void GetLineColumn_counts_lines_and_columns_from_one()
    {
        //Act
        JinjaLexer.GetLineColumn("ab\ncde\nf", 5, out int line, out int column);

        //Assert
        line.Should().Be(2);
        column.Should().Be(3);
    }

    /// <summary>An unterminated tag is a syntax error with a position.</summary>
    [Fact]
    public void Tokenize_with_an_unclosed_tag_throws()
    {
        //Arrange
        System.Action act = () => JinjaLexer.Tokenize("{{ x ");

        //Assert
        act.Should().Throw<ChatTemplateException>();
    }

    /// <summary>lstrip_blocks leaves alone the spaces that do not begin a line.</summary>
    [Fact]
    public void Tokenize_keeps_spaces_that_do_not_begin_a_line()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("{{ x }}   {% if a %}");

        //Assert
        tokens[3].Kind.Should().Be(JinjaTokenKind.Text);
        tokens[3].Value.Should().Be("   ");
    }

    /// <summary>lstrip_blocks still eats the indent that does begin a line.</summary>
    [Fact]
    public void Tokenize_drops_the_indent_that_begins_a_line()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("a\n   {% if b %}");

        //Assert
        tokens[0].Kind.Should().Be(JinjaTokenKind.Text);
        tokens[0].Value.Should().Be("a\n");
    }

    /// <summary>A comment body that ends in a dash is not a whitespace-control marker.</summary>
    [Fact]
    public void Tokenize_with_a_comment_body_ending_in_a_dash_keeps_the_following_text()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize("x{# a - #}   y");

        //Assert
        tokens.Should().HaveCount(3);
        tokens[1].Value.Should().Be("   y");
    }

    /// <summary>A surrogate pair spelled as two escapes becomes the one character it stands for.</summary>
    [Fact]
    public void Tokenize_with_a_high_unicode_escape_builds_the_character()
    {
        //Act
        IReadOnlyList<JinjaToken> tokens = JinjaLexer.Tokenize(@"{{ '\U0001F600' }}");

        //Assert
        tokens[1].Kind.Should().Be(JinjaTokenKind.String);
        tokens[1].Value.Should().Be("\U0001F600");
    }

    /// <summary>A code point outside the Unicode range is a syntax error, not a crash.</summary>
    /// <param name="source">The template.</param>
    [Theory]
    [InlineData(@"{{ '\U0011FFFF' }}")]
    [InlineData(@"{{ '\UFFFFFFFF' }}")]
    [InlineData(@"{{ '\U0000D800' }}")]
    public void Tokenize_with_an_out_of_range_unicode_escape_throws(string source)
    {
        //Arrange
        System.Action act = () => JinjaLexer.Tokenize(source);

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("Jinja template syntax error");
    }
}
