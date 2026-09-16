// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// An identifier, which in a Go template is always a function name.
/// </summary>
internal sealed class GoIdentifierNode : GoNode
{
    /// <summary>Creates an identifier node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="ident">The function name.</param>
    internal GoIdentifierNode(int position, string ident) : base(GoNodeType.Identifier, position)
    {
        Ident = ident;
    }

    /// <summary>The function name.</summary>
    internal string Ident { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoIdentifierNode(Position, Ident);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder) => builder.Append(Ident);
}
