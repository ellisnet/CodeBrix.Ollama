// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/lex.go (BSD-3-Clause);

/// <summary>
/// One token produced by the Go text/template scanner.
/// </summary>
internal readonly struct GoTemplateItem
{
    /// <summary>Creates a token.</summary>
    /// <param name="type">The kind of token.</param>
    /// <param name="position">The starting offset of the token in the input.</param>
    /// <param name="value">The token text.</param>
    /// <param name="line">The 1-based line the token starts on.</param>
    internal GoTemplateItem(GoTemplateItemType type, int position, string value, int line)
    {
        Type = type;
        Position = position;
        Value = value;
        Line = line;
    }

    /// <summary>The kind of token.</summary>
    internal GoTemplateItemType Type { get; }

    /// <summary>The starting offset of the token in the input.</summary>
    internal int Position { get; }

    /// <summary>The token text.</summary>
    internal string Value { get; }

    /// <summary>The 1-based line the token starts on.</summary>
    internal int Line { get; }

    /// <summary>
    /// Renders the token the way Go's <c>item.String</c> does, which is what the parser's error
    /// messages quote.
    /// </summary>
    /// <returns>The rendered token.</returns>
    public override string ToString()
    {
        if (Type == GoTemplateItemType.Eof)
        {
            return "EOF";
        }

        if (Type == GoTemplateItemType.Error)
        {
            return Value;
        }

        if (Type > GoTemplateItemType.Keyword)
        {
            return "<" + Value + ">";
        }

        string value = Value ?? string.Empty;
        if (value.Length > 10)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}...", GoQuote.Quote(Truncate(value, 10)));
        }

        return GoQuote.Quote(value);
    }

    private static string Truncate(string value, int length)
        => value.Length <= length ? value : value.Substring(0, length);
}
