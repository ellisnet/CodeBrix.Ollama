using System;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the thinking parser against every case in Ollama's own parser tests, plus chunk-boundary tests
/// that prove the same text produces the same split however it is cut up.
/// </summary>
public sealed class ThinkingParserTests
{
    /// <summary>A whole response is split into reasoning and content.</summary>
    [Theory]
    [InlineData("<think> internal </think> world", "internal ", "world")]
    [InlineData("<think>a</think><think>b</think>c", "a", "<think>b</think>c")]
    [InlineData("no think", "", "no think")]
    public void AddContent_splits_a_whole_response(string input, string expectedThinking,
        string expectedContent)
    {
        //Arrange
        ThinkingParser parser = new ThinkingParser();

        //Act
        (string thinking, string content) = parser.AddContent(input);

        //Assert
        thinking.Should().Be(expectedThinking);
        content.Should().Be(expectedContent);
    }

    /// <summary>Output with no thinking tag is content, and the buffer is not replayed.</summary>
    [Fact]
    public void AddContent_content_without_a_thinking_tag()
        => AssertSteps(new ThinkingParser(),
            ("  abc", "", "  abc", ThinkingParserState.ThinkingDone),
            ("def", "", "def", ThinkingParserState.ThinkingDone));

    /// <summary>Content before a thinking tag means there is no thinking block at all.</summary>
    [Fact]
    public void AddContent_content_before_a_thinking_tag_cancels_it()
        => AssertSteps(new ThinkingParser(),
            ("  abc <think>def</think> ghi", "", "  abc <think>def</think> ghi",
                ThinkingParserState.ThinkingDone));

    /// <summary>An opening tag arriving a few characters at a time is still recognized.</summary>
    [Fact]
    public void AddContent_building_up_an_opening_tag()
        => AssertSteps(new ThinkingParser(),
            ("  <th", "", "", ThinkingParserState.LookingForOpening),
            ("in", "", "", ThinkingParserState.LookingForOpening),
            ("k>a", "a", "", ThinkingParserState.Thinking));

    /// <summary>A half-arrived closing tag is held back until it completes.</summary>
    [Fact]
    public void AddContent_partial_closing_tag()
        => AssertSteps(new ThinkingParser(),
            ("<think>abc</th", "abc", "", ThinkingParserState.Thinking),
            ("ink>def", "", "def", ThinkingParserState.ThinkingDone));

    /// <summary>Text that only looked like a closing tag is released as reasoning.</summary>
    [Fact]
    public void AddContent_partial_closing_tag_that_turns_out_not_to_be_one()
        => AssertSteps(new ThinkingParser(),
            ("<think>abc</th", "abc", "", ThinkingParserState.Thinking),
            ("ing>def", "</thing>def", "", ThinkingParserState.Thinking),
            ("ghi</thi", "ghi", "", ThinkingParserState.Thinking),
            ("nk>jkl", "", "jkl", ThinkingParserState.ThinkingDone));

    /// <summary>Whitespace between the closing tag and the content is eaten.</summary>
    [Fact]
    public void AddContent_whitespace_after_the_closing_tag()
        => AssertSteps(new ThinkingParser(),
            ("  <think>abc</think>\n\ndef", "abc", "def", ThinkingParserState.ThinkingDone));

    /// <summary>The same, when the whitespace arrives in its own chunk.</summary>
    [Fact]
    public void AddContent_whitespace_after_the_closing_tag_incrementally()
        => AssertSteps(new ThinkingParser(),
            ("  <think>abc</think>", "abc", "", ThinkingParserState.ThinkingDoneEatingWhitespace),
            ("\n\ndef", "", "def", ThinkingParserState.ThinkingDone));

    /// <summary>Whitespace inside the content itself is left alone.</summary>
    [Fact]
    public void AddContent_whitespace_inside_the_content_is_kept()
        => AssertSteps(new ThinkingParser(),
            ("  <think>abc</think>\n\ndef ", "abc", "def ", ThinkingParserState.ThinkingDone),
            (" ghi", "", " ghi", ThinkingParserState.ThinkingDone));

    /// <summary>A response arriving one token at a time walks through every state in turn.</summary>
    [Fact]
    public void AddContent_token_by_token()
        => AssertSteps(new ThinkingParser(),
            ("<think>", "", "", ThinkingParserState.ThinkingStartedEatingWhitespace),
            ("\n", "", "", ThinkingParserState.ThinkingStartedEatingWhitespace),
            ("</think>", "", "", ThinkingParserState.ThinkingDoneEatingWhitespace),
            ("\n\n", "", "", ThinkingParserState.ThinkingDoneEatingWhitespace),
            ("Hi", "", "Hi", ThinkingParserState.ThinkingDone),
            (" there", "", " there", ThinkingParserState.ThinkingDone));

    /// <summary>Whitespace between the opening tag and the reasoning is eaten.</summary>
    [Fact]
    public void AddContent_leading_thinking_whitespace()
        => AssertSteps(new ThinkingParser(),
            ("  <think>   \t ", "", "", ThinkingParserState.ThinkingStartedEatingWhitespace),
            ("  these are some ", "these are some ", "", ThinkingParserState.Thinking),
            ("thoughts </think>  ", "thoughts ", "", ThinkingParserState.ThinkingDoneEatingWhitespace),
            ("  more content", "", "more content", ThinkingParserState.ThinkingDone));

    /// <summary>A parser that starts inside a thinking block reads the first token as reasoning.</summary>
    [Fact]
    public void AddContent_starting_inside_thinking()
    {
        //Arrange
        ThinkingParser parser = new ThinkingParser("<think>", "</think>", true);

        //Act
        (string thinking, string content) = parser.AddContent("reasoning</think>answer");

        //Assert
        parser.State.Should().Be(ThinkingParserState.ThinkingDone);
        thinking.Should().Be("reasoning");
        content.Should().Be("answer");
    }

    /// <summary>A custom tag pair is honoured.</summary>
    [Fact]
    public void AddContent_honours_a_custom_tag_pair()
    {
        //Arrange
        ThinkingParser parser = new ThinkingParser("<|START_THINKING|>", "<|END_THINKING|>");

        //Act
        (string thinking, string content) = parser.AddContent("<|START_THINKING|>why<|END_THINKING|>what");

        //Assert
        thinking.Should().Be("why");
        content.Should().Be("what");
    }

    /// <summary>Text held back when the stream ends comes out of the flush.</summary>
    [Fact]
    public void Flush_returns_the_text_that_never_resolved()
    {
        //Arrange
        ThinkingParser parser = new ThinkingParser();
        parser.AddContent("<think>abc</th");

        //Act
        string remainder = parser.Flush();

        //Assert
        parser.State.Should().Be(ThinkingParserState.Thinking);
        remainder.Should().Be("</th");
        parser.HasBufferedText.Should().BeFalse();
    }

    /// <summary>A tag that is empty or nothing but whitespace is refused.</summary>
    [Theory]
    [InlineData("", "</think>")]
    [InlineData("   ", "</think>")]
    [InlineData("<think>", "")]
    [InlineData("<think>", " \t ")]
    public void Constructor_rejects_an_empty_tag(string openingTag, string closingTag)
    {
        //Arrange
        Action act = () => new ThinkingParser(openingTag, closingTag);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Whitespace that never leads to an opening tag is handed over once the cap is passed.</summary>
    [Fact]
    public void AddContent_gives_up_on_endless_whitespace()
    {
        //Arrange
        ThinkingParser parser = new ThinkingParser();
        string filler = new string(' ', 1024);
        StringBuilder thinking = new StringBuilder();
        StringBuilder content = new StringBuilder();

        //Act
        for (int i = 0; i < 8; i++)
        {
            (string stepThinking, string stepContent) = parser.AddContent(filler);
            thinking.Append(stepThinking);
            content.Append(stepContent);
        }

        //Assert
        thinking.ToString().Should().BeEmpty();
        content.ToString().Should().Be(new string(' ', 5 * 1024));
        parser.Flush().Should().Be(new string(' ', 3 * 1024));
    }

    /// <summary>Nothing is buffered once a response has been fully consumed.</summary>
    [Fact]
    public void Flush_is_empty_after_a_complete_response()
    {
        //Arrange
        ThinkingParser parser = new ThinkingParser();
        parser.AddContent("<think>abc</think>def");

        //Act
        string remainder = parser.Flush();

        //Assert
        remainder.Should().BeEmpty();
    }

    /// <summary>Feeding a response one character at a time gives the same split as feeding it whole.</summary>
    [Theory]
    [InlineData("<think> internal </think> world")]
    [InlineData("<think>a</think><think>b</think>c")]
    [InlineData("no think at all, just an answer")]
    [InlineData("  <think>\n\nthinking about it\n\n</think>\n\nthe answer  ")]
    [InlineData("<think>abc</thing>still thinking</think>done")]
    [InlineData("  leading content <think>never opened</think>")]
    public void AddContent_one_character_at_a_time_matches_the_whole_response(string input)
    {
        //Arrange
        (string thinking, string content) whole = Feed(input, input.Length);

        //Act
        (string thinking, string content) single = Feed(input, 1);

        //Assert
        single.thinking.Should().Be(whole.thinking);
        single.content.Should().Be(whole.content);
    }

    /// <summary>Random chunk sizes give the same split as feeding the response whole.</summary>
    [Theory]
    [InlineData("<think> internal </think> world")]
    [InlineData("<think>a</think><think>b</think>c")]
    [InlineData("no think at all, just an answer")]
    [InlineData("  <think>\n\nthinking about it\n\n</think>\n\nthe answer  ")]
    [InlineData("<think>abc</thing>still thinking</think>done")]
    [InlineData("  leading content <think>never opened</think>")]
    public void AddContent_random_chunk_sizes_match_the_whole_response(string input)
    {
        //Arrange
        (string thinking, string content) whole = Feed(input, input.Length);
        Random random = new Random(20260916);

        //Act and Assert
        for (int attempt = 0; attempt < 25; attempt++)
        {
            (string thinking, string content) chunked = FeedRandom(input, random);
            chunked.thinking.Should().Be(whole.thinking);
            chunked.content.Should().Be(whole.content);
        }
    }

    /// <summary>
    /// Runs a sequence of chunks through a parser, checking the reasoning, the content and the state after
    /// each one.
    /// </summary>
    /// <param name="parser">The parser.</param>
    /// <param name="steps">The chunks and what each should produce.</param>
    private static void AssertSteps(ThinkingParser parser,
        params (string input, string thinking, string content, ThinkingParserState state)[] steps)
    {
        for (int i = 0; i < steps.Length; i++)
        {
            (string thinking, string content) = parser.AddContent(steps[i].input);
            thinking.Should().Be(steps[i].thinking);
            content.Should().Be(steps[i].content);
            parser.State.Should().Be(steps[i].state);
        }
    }

    /// <summary>
    /// Feeds a response through a parser in fixed-size chunks and returns everything it produced.
    /// </summary>
    /// <param name="input">The response.</param>
    /// <param name="chunkSize">The chunk size.</param>
    /// <returns>The reasoning and the content.</returns>
    private static (string thinking, string content) Feed(string input, int chunkSize)
    {
        ThinkingParser parser = new ThinkingParser();
        StringBuilder thinking = new StringBuilder();
        StringBuilder content = new StringBuilder();

        for (int offset = 0; offset < input.Length; offset += chunkSize)
        {
            string chunk = input.Substring(offset, Math.Min(chunkSize, input.Length - offset));
            (string stepThinking, string stepContent) = parser.AddContent(chunk);
            thinking.Append(stepThinking);
            content.Append(stepContent);
        }

        content.Append(parser.Flush());
        return (thinking.ToString(), content.ToString());
    }

    /// <summary>
    /// Feeds a response through a parser in randomly sized chunks and returns everything it produced.
    /// </summary>
    /// <param name="input">The response.</param>
    /// <param name="random">The chunk size source.</param>
    /// <returns>The reasoning and the content.</returns>
    private static (string thinking, string content) FeedRandom(string input, Random random)
    {
        ThinkingParser parser = new ThinkingParser();
        StringBuilder thinking = new StringBuilder();
        StringBuilder content = new StringBuilder();

        int offset = 0;
        while (offset < input.Length)
        {
            int size = Math.Min(random.Next(1, 8), input.Length - offset);
            (string stepThinking, string stepContent) = parser.AddContent(input.Substring(offset, size));
            thinking.Append(stepThinking);
            content.Append(stepContent);
            offset += size;
        }

        content.Append(parser.Flush());
        return (thinking.ToString(), content.ToString());
    }
}
