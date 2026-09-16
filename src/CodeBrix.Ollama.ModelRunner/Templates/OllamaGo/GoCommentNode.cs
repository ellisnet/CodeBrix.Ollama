using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A template comment. It only reaches the tree when comments are being kept.
/// </summary>
internal sealed class GoCommentNode : GoNode
{
    /// <summary>Creates a comment node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="text">The comment text, delimiters excluded.</param>
    internal GoCommentNode(int position, string text) : base(GoNodeType.Comment, position)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>The comment text.</summary>
    internal string Text { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoCommentNode(Position, Text);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        builder.Append("{{");
        builder.Append(Text);
        builder.Append("}}");
    }
}
