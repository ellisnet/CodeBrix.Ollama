// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go (BSD-3-Clause);

/// <summary>
/// A Go slice. <see cref="Stringer"/> and <see cref="JsonMarshaler"/> stand in for a slice type that
/// declares its own <c>String</c> or <c>MarshalJSON</c> method, which several of the types Ollama hands
/// to a template do.
/// </summary>
internal sealed class GoSlice
{
    /// <summary>Creates an empty slice.</summary>
    internal GoSlice()
    {
    }

    /// <summary>Creates a slice over the given items.</summary>
    /// <param name="items">The items, in order.</param>
    internal GoSlice(IEnumerable<object> items)
    {
        if (items != null)
        {
            Items.AddRange(items);
        }
    }

    /// <summary>The items, in order.</summary>
    internal List<object> Items { get; } = new List<object>();

    /// <summary>Whether this stands for a nil slice rather than an empty one, which JSON renders as null.</summary>
    internal bool IsNil { get; set; }

    /// <summary>The slice type's own <c>String</c> method, when it has one.</summary>
    internal Func<GoSlice, string> Stringer { get; set; }

    /// <summary>The slice type's own <c>MarshalJSON</c> method, when it has one.</summary>
    internal Func<GoSlice, string> JsonMarshaler { get; set; }
}
