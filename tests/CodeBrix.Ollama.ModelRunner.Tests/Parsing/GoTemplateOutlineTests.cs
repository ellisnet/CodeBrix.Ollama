using System.Collections.Generic;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the Go template outline against the shapes the thinking and tool-call heuristics rely on, and
/// against the malformed templates a model card occasionally ships.
/// </summary>
public sealed class GoTemplateOutlineTests
{
    /// <summary>A well formed block yields its body.</summary>
    [Fact]
    public void Parse_reads_a_block_body()
    {
        //Act
        IList<GoTemplateOutlineNode> nodes = GoTemplateOutline.Parse("a{{if .X}}b{{end}}c");

        //Assert
        nodes.Should().HaveCount(3);
        nodes[1].Kind.Should().Be(GoTemplateOutlineKind.If);
        LiteralText(nodes).Should().Be("abc");
    }

    /// <summary>A stray end with no block to close does not cut the rest of the template off.</summary>
    [Theory]
    [InlineData("before{{end}}after", "beforeafter")]
    [InlineData("before{{else}}after", "beforeafter")]
    [InlineData("{{end}}{{if .X}}a{{end}}tail", "atail")]
    public void Parse_a_stray_end_does_not_truncate(string template, string expected)
    {
        //Act
        IList<GoTemplateOutlineNode> nodes = GoTemplateOutline.Parse(template);

        //Assert
        LiteralText(nodes).Should().Be(expected);
    }

    /// <summary>A comment is a comment whether or not a trim marker comes first.</summary>
    [Theory]
    [InlineData("A {{/* it's a comment */}} B", "A  B")]
    [InlineData("A {{- /* it's a comment */ -}} B", "AB")]
    [InlineData("A {{- /* a }} brace */ -}} B", "AB")]
    [InlineData("A {{-   /* spaced */ -}} B", "AB")]
    public void Parse_a_comment_after_a_trim_marker(string template, string expected)
    {
        //Act
        IList<GoTemplateOutlineNode> nodes = GoTemplateOutline.Parse(template);

        //Assert
        LiteralText(nodes).Should().Be(expected);
    }

    /// <summary>
    /// Joins the literal text of every text node in a list, so that a template's visible output can be
    /// compared in one assertion.
    /// </summary>
    /// <param name="nodes">The outline nodes.</param>
    /// <returns>The concatenated literal text.</returns>
    private static string LiteralText(IList<GoTemplateOutlineNode> nodes)
    {
        StringBuilder text = new StringBuilder();
        foreach (GoTemplateOutlineNode node in nodes)
        {
            if (node.Kind == GoTemplateOutlineKind.Text)
            {
                text.Append(node.Text);
            }

            text.Append(LiteralText(node.Body));
            if (node.ElseBody != null)
            {
                text.Append(LiteralText(node.ElseBody));
            }
        }

        return text.ToString();
    }
}
