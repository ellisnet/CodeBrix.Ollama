// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A variable reference such as <c>$x</c>, possibly with chained field accesses (<c>$x.A.B</c>). The
/// dollar sign is part of the first name.
/// </summary>
internal sealed class GoVariableNode : GoNode
{
    /// <summary>Creates a variable node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="ident">The variable text, for example <c>$x.A</c>.</param>
    internal GoVariableNode(int position, string ident) : base(GoNodeType.Variable, position)
    {
        Ident = (ident ?? string.Empty).Split('.');
    }

    /// <summary>The variable name followed by the chained field names, in lexical order.</summary>
    internal string[] Ident { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoVariableNode(Position, string.Join(".", Ident));

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        for (int i = 0; i < Ident.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('.');
            }

            builder.Append(Ident[i]);
        }
    }
}
