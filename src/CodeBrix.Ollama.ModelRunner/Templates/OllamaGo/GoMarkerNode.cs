// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// An <c>{{end}}</c> or <c>{{else}}</c> marker. The parser returns these to close an item list and they
/// never reach a finished tree, so Go's separate endNode and elseNode are one type here.
/// </summary>
internal sealed class GoMarkerNode : GoNode
{
    /// <summary>Creates a marker node.</summary>
    /// <param name="type">Either <see cref="GoNodeType.End"/> or <see cref="GoNodeType.Else"/>.</param>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="line">The 1-based line the marker starts on.</param>
    internal GoMarkerNode(GoNodeType type, int position, int line) : base(type, position)
    {
        Line = line;
    }

    /// <summary>The 1-based line the marker starts on.</summary>
    internal int Line { get; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoMarkerNode(NodeType, Position, Line);

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
        => builder.Append(NodeType == GoNodeType.End ? "{{end}}" : "{{else}}");
}
