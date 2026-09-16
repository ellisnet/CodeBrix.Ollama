// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// A numeric constant. Go stores the value under every type that can represent it, which is how it
/// mimics ideal constants; this does the same for the integer and floating-point types.
/// </summary>
internal sealed class GoNumberNode : GoNode
{
    /// <summary>Creates a numeric node.</summary>
    /// <param name="position">The offset of the start of the node in the template text.</param>
    /// <param name="text">The original text of the constant.</param>
    internal GoNumberNode(int position, string text) : base(GoNodeType.Number, position)
    {
        Text = text;
    }

    /// <summary>The original text of the constant.</summary>
    internal string Text { get; }

    /// <summary>Whether the constant has an integral value.</summary>
    internal bool IsInt { get; set; }

    /// <summary>Whether the constant has an unsigned integral value.</summary>
    internal bool IsUint { get; set; }

    /// <summary>Whether the constant has a floating-point value.</summary>
    internal bool IsFloat { get; set; }

    /// <summary>The signed integer value.</summary>
    internal long Int64 { get; set; }

    /// <summary>The unsigned integer value.</summary>
    internal ulong Uint64 { get; set; }

    /// <summary>The floating-point value.</summary>
    internal double Float64 { get; set; }

    /// <inheritdoc />
    internal override GoNode Copy()
        => new GoNumberNode(Position, Text)
        {
            IsInt = IsInt,
            IsUint = IsUint,
            IsFloat = IsFloat,
            Int64 = Int64,
            Uint64 = Uint64,
            Float64 = Float64,
        };

    /// <inheritdoc />
    internal override void WriteTo(StringBuilder builder) => builder.Append(Text);
}
