// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A boolean constant.
/// </summary>
internal sealed class GoBoolNode : GoNode
{
    /// <summary>Creates a boolean node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="value">The value of the constant.</param>
    internal GoBoolNode(int position, bool value) : base(GoNodeType.Bool, position)
    {
        True = value;
    }

    /// <summary>The value of the constant.</summary>
    internal bool True { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoBoolNode(Position, True);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder) => builder.Append(True ? "true" : "false");
}
