// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A pipeline: an optional declaration or assignment followed by one or more commands joined by
/// <c>|</c>.
/// </summary>
internal sealed class GoPipeNode : GoNode
{
    /// <summary>Creates an empty pipeline node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="line">The 1-based line the pipeline starts on.</param>
    internal GoPipeNode(int position, int line) : base(GoNodeType.Pipe, position)
    {
        Line = line;
    }

    /// <summary>The 1-based line the pipeline starts on.</summary>
    internal int Line { get; }

    /// <summary>Whether the variables are being assigned rather than declared.</summary>
    internal bool IsAssign { get; set; }

    /// <summary>The declared or assigned variables, in lexical order.</summary>
    internal List<GoVariableNode> Decl { get; } = new List<GoVariableNode>();

    /// <summary>The commands, in lexical order.</summary>
    internal List<GoCommandNode> Cmds { get; set; } = new List<GoCommandNode>();

    /// <summary>Appends a command.</summary>
    /// <param name="command">The command to append.</param>
    internal void Append(GoCommandNode command) => Cmds.Add(command);

    /// <summary>Deep-copies the pipeline.</summary>
    /// <returns>The copy, or <see langword="null"/> when this pipeline is <see langword="null"/>.</returns>
    internal GoPipeNode CopyPipe()
    {
        GoPipeNode copy = new GoPipeNode(Position, Line) { IsAssign = IsAssign };
        foreach (GoVariableNode decl in Decl)
        {
            copy.Decl.Add((GoVariableNode)decl.Copy());
        }

        foreach (GoCommandNode command in Cmds)
        {
            copy.Append(command.CopyCommand());
        }

        return copy;
    }

    /// <inheritdoc />
    internal override GoNode Copy() => CopyPipe();

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        if (Decl.Count > 0)
        {
            for (int i = 0; i < Decl.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                Decl[i].WriteTo(builder);
            }

            builder.Append(IsAssign ? " = " : " := ");
        }

        for (int i = 0; i < Cmds.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            Cmds[i].WriteTo(builder);
        }
    }
}
