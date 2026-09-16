// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// One stage of a pipeline: a term followed by its space-separated arguments.
/// </summary>
internal sealed class GoCommandNode : GoNode
{
    /// <summary>Creates an empty command node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    internal GoCommandNode(int position) : base(GoNodeType.Command, position)
    {
    }

    /// <summary>The arguments, in lexical order. The first is the term being invoked.</summary>
    internal List<GoNode> Args { get; set; } = new List<GoNode>();

    /// <summary>Appends an argument.</summary>
    /// <param name="arg">The argument node to append.</param>
    internal void Append(GoNode arg) => Args.Add(arg);

    /// <summary>Deep-copies the command.</summary>
    /// <returns>The copy.</returns>
    internal GoCommandNode CopyCommand()
    {
        GoCommandNode copy = new GoCommandNode(Position);
        foreach (GoNode arg in Args)
        {
            copy.Append(arg.Copy());
        }

        return copy;
    }

    /// <inheritdoc />
    internal override GoNode Copy() => CopyCommand();

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        for (int i = 0; i < Args.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            if (Args[i] is GoPipeNode pipe)
            {
                builder.Append('(');
                pipe.WriteTo(builder);
                builder.Append(')');
                continue;
            }

            Args[i].WriteTo(builder);
        }
    }
}
