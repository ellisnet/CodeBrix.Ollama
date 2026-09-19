using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Covers the merge table of a GPT-2 byte-level BPE tokenizer: the first-line rule and what a malformed line
/// does.
/// </summary>
public sealed class Gpt2MergeTableTests
{
    [Fact]
    public void Parse_drops_the_first_line_only_when_it_is_a_comment()
    {
        //Arrange
        string[] withHeader = { "#version: 0.2", "a b", "c d" };
        string[] withoutHeader = { "a b", "c d" };

        //Act
        List<string> headed = Gpt2MergeTable.Parse(withHeader);
        List<string> bare = Gpt2MergeTable.Parse(withoutHeader);

        //Assert
        headed.Should().BeEquivalentTo(new[] { "a b", "c d" });
        bare.Should().BeEquivalentTo(new[] { "a b", "c d" });
    }

    [Fact]
    public void Parse_ignores_a_malformed_line_rather_than_refusing_the_file()
    {
        //Act
        List<string> merges = Gpt2MergeTable.Parse(new[] { "a b", "three parts here", "", "c d" });

        //Assert
        merges.Should().BeEquivalentTo(new[] { "a b", "c d" });
    }

    [Fact]
    public void Parse_keeps_the_rank_order_the_file_gave() =>
        Gpt2MergeTable.Parse(new[] { "t h", "th e", "a n" })
            .Should().Equal(new[] { "t h", "th e", "a n" });

    [Fact]
    public void Parse_with_no_lines_returns_no_merges() =>
        Gpt2MergeTable.Parse(new string[0]).Should().BeEmpty();
}
