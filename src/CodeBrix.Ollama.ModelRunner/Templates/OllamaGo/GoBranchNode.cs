// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// An <c>if</c>, <c>range</c> or <c>with</c> action. Go models these as a shared BranchNode with three
/// thin wrappers around it; the wrappers carry nothing but their node type, so this is one type whose
/// <see cref="GoNode.NodeType"/> says which it is.
/// </summary>
internal sealed class GoBranchNode : GoNode
{
    /// <summary>Creates a branch node.</summary>
    /// <param name="type">One of <see cref="GoNodeType.If"/>, <see cref="GoNodeType.Range"/> or
    /// <see cref="GoNodeType.With"/>.</param>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="line">The 1-based line the action starts on.</param>
    /// <param name="pipe">The pipeline to evaluate.</param>
    /// <param name="list">What to execute when the value is non-empty.</param>
    /// <param name="elseList">What to execute when the value is empty, or <see langword="null"/> when
    /// there is no else clause.</param>
    internal GoBranchNode(GoNodeType type, int position, int line, GoPipeNode pipe, GoListNode list,
        GoListNode elseList) : base(type, position)
    {
        Line = line;
        Pipe = pipe;
        List = list;
        ElseList = elseList;
    }

    /// <summary>The 1-based line the action starts on.</summary>
    internal int Line { get; }

    /// <summary>The pipeline to evaluate.</summary>
    internal GoPipeNode Pipe { get; set; }

    /// <summary>What to execute when the value is non-empty.</summary>
    internal GoListNode List { get; set; }

    /// <summary>What to execute when the value is empty; <see langword="null"/> when there is no else clause.</summary>
    internal GoListNode ElseList { get; set; }

    /// <inheritdoc />
    internal override GoNode Copy()
        => new GoBranchNode(NodeType, Position, Line, Pipe.CopyPipe(), List.CopyList(), ElseList?.CopyList());

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        string name = NodeType == GoNodeType.If ? "if" : NodeType == GoNodeType.Range ? "range" : "with";
        builder.Append("{{");
        builder.Append(name);
        builder.Append(' ');
        Pipe.WriteTo(builder);
        builder.Append("}}");
        List.WriteTo(builder);
        if (ElseList != null)
        {
            builder.Append("{{else}}");
            ElseList.WriteTo(builder);
        }

        builder.Append("{{end}}");
    }
}
