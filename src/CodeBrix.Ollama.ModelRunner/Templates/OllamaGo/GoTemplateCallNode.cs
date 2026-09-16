// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A <c>{{template "name" pipeline}}</c> action.
/// </summary>
internal sealed class GoTemplateCallNode : GoNode
{
    /// <summary>Creates a template invocation node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="line">The 1-based line the action starts on.</param>
    /// <param name="name">The name of the template being invoked, unquoted.</param>
    /// <param name="pipe">The pipeline whose value becomes dot inside the invoked template, or
    /// <see langword="null"/> when the action gave none.</param>
    internal GoTemplateCallNode(int position, int line, string name, GoPipeNode pipe)
        : base(GoNodeType.Template, position)
    {
        Line = line;
        Name = name;
        Pipe = pipe;
    }

    /// <summary>The 1-based line the action starts on.</summary>
    internal int Line { get; }

    /// <summary>The name of the template being invoked, unquoted.</summary>
    internal string Name { get; }

    /// <summary>The pipeline whose value becomes dot inside the invoked template.</summary>
    internal GoPipeNode Pipe { get; set; }

    /// <inheritdoc />
    internal override GoNode Copy() => new GoTemplateCallNode(Position, Line, Name, Pipe?.CopyPipe());

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder)
    {
        builder.Append("{{template ");
        builder.Append(GoQuote.Quote(Name));
        if (Pipe != null)
        {
            builder.Append(' ');
            Pipe.WriteTo(builder);
        }

        builder.Append("}}");
    }
}
