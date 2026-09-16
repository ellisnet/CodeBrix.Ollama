// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A field access on dot, such as <c>.A</c> or <c>.A.B</c>. The periods are dropped from each name.
/// </summary>
internal sealed class GoFieldNode : GoNode
{
    /// <summary>Creates a field node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="ident">The field text including its leading period, for example <c>.A.B</c>.</param>
    internal GoFieldNode(int position, string ident) : base(GoNodeType.Field, position)
    {
        string text = ident ?? string.Empty;
        Ident = (text.Length > 0 ? text.Substring(1) : text).Split('.');
    }

    /// <summary>The field names, in lexical order, without their periods.</summary>
    internal string[] Ident { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoFieldNode(Position, "." + string.Join(".", Ident));

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        foreach (string id in Ident)
        {
            builder.Append('.');
            builder.Append(id);
        }
    }
}
