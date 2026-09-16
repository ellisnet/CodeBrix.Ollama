using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// The untyped <c>nil</c> constant.
/// </summary>
internal sealed class GoNilNode : GoNode
{
    /// <summary>Creates a nil node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    internal GoNilNode(int position) : base(GoNodeType.Nil, position)
    {
    }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoNilNode(Position);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder) => builder.Append("nil");
}
