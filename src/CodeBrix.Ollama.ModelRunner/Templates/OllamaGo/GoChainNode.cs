// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A term followed by a chain of field accesses, as in <c>(pipeline).A.B</c>. The periods are dropped
/// from each name.
/// </summary>
internal sealed class GoChainNode : GoNode
{
    /// <summary>Creates a chain node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="node">The term the fields are read from.</param>
    internal GoChainNode(int position, GoNode node) : base(GoNodeType.Chain, position)
    {
        Node = node;
    }

    /// <summary>The term the fields are read from.</summary>
    internal GoNode Node { get; }

    /// <summary>The field names, in lexical order, without their periods.</summary>
    internal List<string> Field { get; } = new List<string>();

    /// <summary>Adds a field to the end of the chain.</summary>
    /// <param name="field">The field text including its leading period.</param>
    internal void Add(string field) => Field.Add(field.Substring(1));

    /// <inheritdoc />
    internal override GoNode Copy()
    {
        GoChainNode copy = new GoChainNode(Position, Node);
        copy.Field.AddRange(Field);
        return copy;
    }

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        if (Node is GoPipeNode)
        {
            builder.Append('(');
            Node.WriteTo(builder);
            builder.Append(')');
        }
        else
        {
            Node.WriteTo(builder);
        }

        foreach (string field in Field)
        {
            builder.Append('.');
            builder.Append(field);
        }
    }
}
