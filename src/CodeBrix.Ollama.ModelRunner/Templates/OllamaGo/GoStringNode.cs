// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A string constant. The value has already been unquoted.
/// </summary>
internal sealed class GoStringNode : GoNode
{
    /// <summary>Creates a string node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="quoted">The original text of the constant, quotes included.</param>
    /// <param name="text">The constant after quote processing.</param>
    internal GoStringNode(int position, string quoted, string text) : base(GoNodeType.String, position)
    {
        Quoted = quoted;
        Text = text;
    }

    /// <summary>The original text of the constant, quotes included.</summary>
    internal string Quoted { get; }

    /// <summary>The constant after quote processing.</summary>
    internal string Text { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoStringNode(Position, Quoted, Text);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder) => builder.Append(Quoted);
}
