using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// The cursor, written as a bare period.
/// </summary>
internal sealed class GoDotNode : GoNode
{
    /// <summary>Creates a dot node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    internal GoDotNode(int position) : base(GoNodeType.Dot, position)
    {
    }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoDotNode(Position);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder) => builder.Append('.');
}
