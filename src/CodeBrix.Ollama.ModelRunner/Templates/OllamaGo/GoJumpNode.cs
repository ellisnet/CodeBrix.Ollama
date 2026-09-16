using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A <c>{{break}}</c> or <c>{{continue}}</c> action inside a range loop. Go has a node type for each;
/// they behave identically apart from which loop control they trigger, so they are one type here.
/// </summary>
internal sealed class GoJumpNode : GoNode
{
    /// <summary>Creates a jump node.</summary>
    /// <param name="type">Either <see cref="GoNodeType.Break"/> or <see cref="GoNodeType.Continue"/>.</param>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="line">The 1-based line the action starts on.</param>
    internal GoJumpNode(GoNodeType type, int position, int line) : base(type, position)
    {
        Line = line;
    }

    /// <summary>The 1-based line the action starts on.</summary>
    internal int Line { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoJumpNode(NodeType, Position, Line);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
        => builder.Append(NodeType == GoNodeType.Break ? "{{break}}" : "{{continue}}");
}
