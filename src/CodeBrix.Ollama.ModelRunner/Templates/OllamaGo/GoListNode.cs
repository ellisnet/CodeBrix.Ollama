using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A sequence of nodes - the body of a template, of a branch, or of an else clause.
/// </summary>
internal sealed class GoListNode : GoNode
{
    /// <summary>Creates an empty list node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    internal GoListNode(int position) : base(GoNodeType.List, position)
    {
    }

    /// <summary>The element nodes, in lexical order.</summary>
    internal List<GoNode> Nodes { get; set; } = new List<GoNode>();

    /// <summary>Appends a node to the list.</summary>
    /// <param name="node">The node to append.</param>
    internal void Append(GoNode node) => Nodes.Add(node);

    /// <summary>Deep-copies the list.</summary>
    /// <returns>The copy.</returns>
    internal GoListNode CopyList()
    {
        GoListNode copy = new GoListNode(Position);
        foreach (GoNode node in Nodes)
        {
            copy.Append(node.Copy());
        }

        return copy;
    }

    /// <inheritdoc />
    internal override GoNode Copy() => CopyList();

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        foreach (GoNode node in Nodes)
        {
            node.WriteTo(builder);
        }
    }
}
