using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// One element of a parsed Go text/template.
/// </summary>
internal abstract class GoNode
{
    /// <summary>Creates a node.</summary>
    /// <param name="type">The kind of node.</param>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    protected GoNode(GoNodeType type, int position)
    {
        NodeType = type;
        Position = position;
    }

    /// <summary>The kind of node.</summary>
    internal GoNodeType NodeType { get; }

    /// <summary>The offset of the start of the node in the template text.</summary>
    internal int Position { get; }

    /// <summary>Deep-copies the node and everything below it.</summary>
    /// <returns>The copy.</returns>
    internal abstract GoNode Copy();

    /// <summary>Writes the node's template source form to a builder.</summary>
    /// <param name="builder">The builder to write to.</param>
    internal abstract void WriteTo(StringBuilder builder);

    /// <summary>Renders the node back to template source.</summary>
    /// <returns>The template source of this node.</returns>
    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        WriteTo(builder);
        return builder.ToString();
    }
}
