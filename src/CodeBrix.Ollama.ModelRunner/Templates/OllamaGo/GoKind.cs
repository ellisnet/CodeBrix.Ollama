// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go (BSD-3-Clause);

/// <summary>
/// The kind of a value inside a rendering template, mirroring the subset of Go's reflect kinds that a
/// chat template can produce.
/// </summary>
internal enum GoKind
{
    /// <summary>No value at all - Go's zero <c>reflect.Value</c>, which prints as <c>&lt;no value&gt;</c>.</summary>
    Invalid = 0,

    /// <summary>A nil interface, which prints as <c>&lt;nil&gt;</c>.</summary>
    Nil,

    /// <summary>A boolean.</summary>
    Bool,

    /// <summary>A signed integer.</summary>
    Int,

    /// <summary>An unsigned integer.</summary>
    Uint,

    /// <summary>A floating-point number.</summary>
    Float,

    /// <summary>A string.</summary>
    String,

    /// <summary>A slice or array.</summary>
    Slice,

    /// <summary>A map.</summary>
    Map,

    /// <summary>A struct.</summary>
    Struct,
}
