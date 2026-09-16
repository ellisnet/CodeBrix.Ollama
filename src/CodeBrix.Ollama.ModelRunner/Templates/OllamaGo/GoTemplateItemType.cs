// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/lex.go (BSD-3-Clause);

/// <summary>
/// The kind of token the Go text/template scanner produces. The order matters: everything after
/// <see cref="Keyword"/> is a keyword, exactly as in Go's <c>itemType</c>.
/// </summary>
internal enum GoTemplateItemType
{
    /// <summary>An error occurred; the value is the text of the error.</summary>
    Error = 0,

    /// <summary>A boolean constant.</summary>
    Bool,

    /// <summary>A printable ASCII character; the grab bag for comma and friends.</summary>
    Char,

    /// <summary>A character constant, quotes included.</summary>
    CharConstant,

    /// <summary>Comment text.</summary>
    Comment,

    /// <summary>A complex constant such as 1+2i.</summary>
    Complex,

    /// <summary>An equals sign introducing an assignment.</summary>
    Assign,

    /// <summary>A colon-equals introducing a declaration.</summary>
    Declare,

    /// <summary>The end of the input.</summary>
    Eof,

    /// <summary>An alphanumeric identifier starting with a period.</summary>
    Field,

    /// <summary>An alphanumeric identifier not starting with a period.</summary>
    Identifier,

    /// <summary>The left action delimiter.</summary>
    LeftDelim,

    /// <summary>A left parenthesis inside an action.</summary>
    LeftParen,

    /// <summary>A simple number.</summary>
    Number,

    /// <summary>The pipe symbol.</summary>
    Pipe,

    /// <summary>A raw quoted string, back quotes included.</summary>
    RawString,

    /// <summary>The right action delimiter.</summary>
    RightDelim,

    /// <summary>A right parenthesis inside an action.</summary>
    RightParen,

    /// <summary>A run of spaces separating arguments.</summary>
    Space,

    /// <summary>A quoted string, quotes included.</summary>
    String,

    /// <summary>Plain text.</summary>
    Text,

    /// <summary>A variable starting with a dollar sign.</summary>
    Variable,

    /// <summary>Delimits the keywords; everything above this value is a keyword.</summary>
    Keyword,

    /// <summary>The <c>block</c> keyword.</summary>
    Block,

    /// <summary>The <c>break</c> keyword.</summary>
    Break,

    /// <summary>The <c>continue</c> keyword.</summary>
    Continue,

    /// <summary>The cursor, spelled as a bare period.</summary>
    Dot,

    /// <summary>The <c>define</c> keyword.</summary>
    Define,

    /// <summary>The <c>else</c> keyword.</summary>
    Else,

    /// <summary>The <c>end</c> keyword.</summary>
    End,

    /// <summary>The <c>if</c> keyword.</summary>
    If,

    /// <summary>The untyped <c>nil</c> constant, easiest to treat as a keyword.</summary>
    Nil,

    /// <summary>The <c>range</c> keyword.</summary>
    Range,

    /// <summary>The <c>template</c> keyword.</summary>
    Template,

    /// <summary>The <c>with</c> keyword.</summary>
    With,
}
