using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A run of literal text between actions.
/// </summary>
internal sealed class GoTextNode : GoNode
{
    /// <summary>Creates a text node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="text">The literal text, which may span newlines.</param>
    internal GoTextNode(int position, string text) : base(GoNodeType.Text, position)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>The literal text.</summary>
    internal string Text { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoTextNode(Position, Text);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder) => builder.Append(Text);
}
