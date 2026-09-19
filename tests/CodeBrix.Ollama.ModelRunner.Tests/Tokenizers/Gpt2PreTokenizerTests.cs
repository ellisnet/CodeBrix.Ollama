using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The first cut a byte-level byte-pair encoder makes, held to two things: the published pattern itself,
/// compiled as a .NET regular expression over text it can read faithfully, and the cases where a .NET regular
/// expression CANNOT read it and the hand-written scan is the only one that agrees with the published engine.
/// </summary>
public sealed partial class Gpt2PreTokenizerTests
{
    //A mathematical capital A: a LETTER, and outside the basic multilingual plane.
    private const string MathematicalA = "\U0001D400";

    //The next-line character is whitespace to both engines and cannot be written in a string literal as
    //itself, because this language counts it as a line break in source text.
    private const string NextLine = "\u0085";

    /// <summary>
    /// Text whose pieces the published engine and a .NET regular expression agree on, which is all of it
    /// inside the basic multilingual plane.
    /// </summary>
    public static TheoryData<string> BasicPlane() => new TheoryData<string>
    {
        "the quick brown fox",
        " leading space",
        "trailing space ",
        "two  spaces",
        "three   spaces   between   words",
        "don't it's we've I'll he'd they're I'm",
        "'s 't 're 've 'm 'll 'd",
        "'S 'T 'RE",
        "numbers 123 and 4567 and 0",
        "mixed123abc456",
        "a\nb\n\nc",
        "\n \n\n \t\t  ",
        "  ",
        " ",
        "",
        "a   ",
        "   a",
        "punctuation: !?.,;:'\"()[]{}<>/\\|-_=+*&^%$#@~`",
        "Nöldeke and Müller and Ångström",
        "你好世界",
        "  　spaces",
        "" + NextLine + " controls",
        "é and é",
        "word​zero width",
        "①Ⅱ circled and roman numerals",
    };

    /// <summary>The scan agrees with the published pattern, compiled, on text the two can both read.</summary>
    /// <param name="text">The text.</param>
    [Theory]
    [MemberData(nameof(BasicPlane))]
    public void Split_agrees_with_the_published_pattern(string text)
    {
        //Arrange
        List<string> expected = new List<string>();
        foreach (Match match in Published().Matches(text)) expected.Add(match.Value);

        //Act
        List<string> pieces = Gpt2PreTokenizer.Split(text);

        //Assert
        pieces.Should().Equal(expected);
    }

    /// <summary>Whatever the pieces are, putting them back together is the text again.</summary>
    /// <param name="text">The text.</param>
    [Theory]
    [MemberData(nameof(BasicPlane))]
    public void Split_loses_nothing(string text) =>
        string.Concat(Gpt2PreTokenizer.Split(text)).Should().Be(text);

    /// <summary>
    /// A letter outside the basic multilingual plane is a LETTER, which is the case a .NET regular expression
    /// of the published pattern gets wrong - it works in UTF-16 units, where such a letter is a surrogate
    /// pair and neither half of it is in any letter category.
    /// </summary>
    [Fact]
    public void Split_keeps_a_letter_outside_the_basic_plane_with_the_letters()
    {
        //Arrange
        string text = "\U00020000\U00020001 ab";

        //Act
        List<string> pieces = Gpt2PreTokenizer.Split(text);

        //Assert
        pieces.Should().Equal("\U00020000\U00020001", " ab");
    }

    /// <summary>And a .NET regular expression of the same pattern really does disagree, which is why the scan exists.</summary>
    [Fact]
    public void Split_differs_from_the_published_pattern_outside_the_basic_plane()
    {
        //Arrange - an ordinary letter and then one outside the basic plane, which is ONE run of letters.
        string text = "a" + MathematicalA;
        List<string> byPattern = new List<string>();
        foreach (Match match in Published().Matches(text)) byPattern.Add(match.Value);

        //Act
        List<string> pieces = Gpt2PreTokenizer.Split(text);

        //Assert
        pieces.Should().Equal(text);
        byPattern.Should().Equal("a", MathematicalA);
    }

    /// <summary>A digit outside the basic multilingual plane is a NUMBER and keeps its own run.</summary>
    [Fact]
    public void Split_keeps_a_number_outside_the_basic_plane_with_the_numbers() =>
        Gpt2PreTokenizer.Split("\U0001d7ce\U0001d7cf").Should().Equal("\U0001d7ce\U0001d7cf");

    /// <summary>An emoji is neither letter nor number, and it stays whole.</summary>
    [Fact]
    public void Split_keeps_an_emoji_whole() =>
        Gpt2PreTokenizer.Split("\U0001f600\U0001f3bc").Should().Equal("\U0001f600\U0001f3bc");

    /// <summary>A run of spaces gives its LAST one to what follows, which is what puts a space on a word.</summary>
    [Fact]
    public void Split_gives_the_last_space_of_a_run_to_the_word_after_it() =>
        Gpt2PreTokenizer.Split("a   b").Should().Equal("a", "  ", " b");

    /// <summary>A run of whitespace at the end of the text has nothing to give it to and stays whole.</summary>
    [Fact]
    public void Split_keeps_a_run_of_whitespace_at_the_end_whole() =>
        Gpt2PreTokenizer.Split("a   ").Should().Equal("a", "   ");

    /// <summary>A contraction is taken before anything else, and only in the seven forms the pattern names.</summary>
    [Fact]
    public void Split_takes_the_seven_contractions() =>
        Gpt2PreTokenizer.Split("it's we've I'll he'd I'm don't they're")
            .Should().Equal(
                "it", "'s", " we", "'ve", " I", "'ll", " he", "'d", " I", "'m", " don", "'t", " they", "'re");

    /// <summary>
    /// The whitespace the scan recognizes is exactly what a .NET regular expression's <c>\s</c> recognizes,
    /// over every character of the basic multilingual plane - and that in turn is what the published engine
    /// recognizes.
    /// </summary>
    [Fact]
    public void IsWhiteSpace_agrees_with_the_frameworks_own_class()
    {
        //Arrange
        Regex space = new Regex(@"^\s$", RegexOptions.CultureInvariant);
        List<int> differing = new List<int>();

        //Act
        for (int value = 0; value <= 0xFFFF; value++)
        {
            if (value >= 0xD800 && value <= 0xDFFF) continue;

            Rune rune = new Rune(value);
            bool mine = Gpt2PreTokenizer.IsWhiteSpace(rune);
            if (mine != space.IsMatch(((char)value).ToString())) differing.Add(value);
        }

        //Assert
        differing.Should().BeEmpty();
    }

    /// <summary>The letter and number classes are the framework's own categories, over the whole plane.</summary>
    [Fact]
    public void IsLetter_and_IsNumber_agree_with_the_frameworks_own_classes()
    {
        //Arrange
        Regex letter = new Regex(@"^\p{L}$", RegexOptions.CultureInvariant);
        Regex number = new Regex(@"^\p{N}$", RegexOptions.CultureInvariant);
        List<int> differing = new List<int>();

        //Act
        for (int value = 0; value <= 0xFFFF; value++)
        {
            if (value >= 0xD800 && value <= 0xDFFF) continue;

            Rune rune = new Rune(value);
            string one = ((char)value).ToString();
            if (Gpt2PreTokenizer.IsLetter(rune) != letter.IsMatch(one)) differing.Add(value);
            if (Gpt2PreTokenizer.IsNumber(rune) != number.IsMatch(one)) differing.Add(value);
        }

        //Assert
        differing.Should().BeEmpty();
    }

    //The published pattern itself, compiled. It is here in the TESTS and not in the library because .NET's
    //regular-expression engine works in UTF-16 units and the published one works in code points, so this
    //agrees with the library only inside the basic multilingual plane - which is what the theory above uses
    //it for, and what the two tests after it demonstrate.
    [GeneratedRegex(
        @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+",
        RegexOptions.CultureInvariant)]
    private static partial Regex Published();
}
