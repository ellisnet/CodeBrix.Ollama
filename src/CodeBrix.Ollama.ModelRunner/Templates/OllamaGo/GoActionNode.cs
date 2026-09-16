// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A plain action - anything between delimiters that is not a control structure.
/// </summary>
internal sealed class GoActionNode : GoNode
{
    /// <summary>Creates an action node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="line">The 1-based line the action starts on.</param>
    /// <param name="pipe">The pipeline in the action.</param>
    internal GoActionNode(int position, int line, GoPipeNode pipe) : base(GoNodeType.Action, position)
    {
        Line = line;
        Pipe = pipe;
    }

    /// <summary>The 1-based line the action starts on.</summary>
    internal int Line { get; }

    /// <summary>The pipeline in the action.</summary>
    internal GoPipeNode Pipe { get; set; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoActionNode(Position, Line, Pipe.CopyPipe());

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        builder.Append("{{");
        Pipe.WriteTo(builder);
        builder.Append("}}");
    }
}
