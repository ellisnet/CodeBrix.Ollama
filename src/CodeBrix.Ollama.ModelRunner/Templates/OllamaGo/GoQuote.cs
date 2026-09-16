using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/strconv/quote.go (BSD-3-Clause);

/// <summary>
/// Go's <c>strconv</c> quoting and unquoting, which the template scanner, the template parser and the
/// <c>%q</c> verb of <c>printf</c> all rely on.
/// </summary>
internal static class GoQuote
{
    private const int ReplacementRune = 0xFFFD;

    /// <summary>
    /// Quotes text the way Go's <c>strconv.Quote</c> does: double quotes, Go escape sequences for the
    /// characters that need them, printable characters left alone.
    /// </summary>
    /// <param name="value">The text to quote. <see langword="null"/> is treated as empty.</param>
    /// <returns>The quoted text, quotes included.</returns>
    internal static string Quote(string value)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append('"');
        AppendEscaped(builder, value ?? string.Empty, '"');
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Quotes a single code point the way Go's <c>strconv.QuoteRune</c> does.
    /// </summary>
    /// <param name="codePoint">The code point to quote.</param>
    /// <returns>The quoted character, single quotes included.</returns>
    internal static string QuoteRune(int codePoint)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append('\'');
        //Go's strconv replaces a rune that is not valid UTF-8 - a surrogate half, a negative value or
        //anything past U+10FFFF - with the replacement character rather than failing
        AppendEscapedRune(builder, IsValidRune(codePoint) ? codePoint : ReplacementRune, '\'');
        builder.Append('\'');
        return builder.ToString();
    }

    /// <summary>
    /// Undoes <see cref="Quote"/>, and also accepts a back-quoted raw string or a single-quoted
    /// character constant, exactly as Go's <c>strconv.Unquote</c> does.
    /// </summary>
    /// <param name="quoted">The quoted text, quotes included.</param>
    /// <param name="value">The unquoted text, or <see langword="null"/> when the text was malformed.</param>
    /// <returns><see langword="true"/> when the text was unquoted.</returns>
    internal static bool TryUnquote(string quoted, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(quoted) || quoted.Length < 2)
        {
            return false;
        }

        char quote = quoted[0];
        if (quoted[quoted.Length - 1] != quote)
        {
            return false;
        }

        string body = quoted.Substring(1, quoted.Length - 2);
        if (quote == '`')
        {
            if (body.IndexOf('`') >= 0)
            {
                return false;
            }

            value = body.Replace("\r", string.Empty);
            return true;
        }

        if (quote != '"' && quote != '\'')
        {
            return false;
        }

        StringBuilder builder = new StringBuilder(body.Length);
        List<byte> pending = new List<byte>();
        int index = 0;
        int runes = 0;
        while (index < body.Length)
        {
            char c = body[index];
            if (c == '\n')
            {
                return false;
            }

            if (c != '\\')
            {
                FlushBytes(builder, pending);
                if (char.IsHighSurrogate(c) && index + 1 < body.Length && char.IsLowSurrogate(body[index + 1]))
                {
                    builder.Append(c);
                    builder.Append(body[index + 1]);
                    index += 2;
                }
                else
                {
                    builder.Append(c);
                    index++;
                }

                runes++;
                continue;
            }

            if (!TryUnescape(body, ref index, quote, builder, pending))
            {
                return false;
            }

            runes++;
        }

        FlushBytes(builder, pending);
        if (quote == '\'' && runes != 1)
        {
            return false;
        }

        value = builder.ToString();
        return true;
    }

    /// <summary>
    /// Decodes the bytes a run of <c>\xNN</c> or octal escapes produced. Go unquotes those to raw bytes,
    /// so a run of them is one UTF-8 sequence and has to be decoded as a whole.
    /// </summary>
    /// <param name="builder">The text built so far.</param>
    /// <param name="pending">The bytes waiting to be decoded, which this empties.</param>
    private static void FlushBytes(StringBuilder builder, List<byte> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        builder.Append(Encoding.UTF8.GetString(pending.ToArray()));
        pending.Clear();
    }

    private static bool TryUnescape(string body, ref int index, char quote, StringBuilder builder,
        List<byte> pending)
    {
        index++; //past the backslash
        if (index >= body.Length)
        {
            return false;
        }

        char c = body[index++];
        if (c != 'x' && !(c >= '0' && c <= '7'))
        {
            FlushBytes(builder, pending);
        }

        switch (c)
        {
            case 'a': builder.Append('\a'); return true;
            case 'b': builder.Append('\b'); return true;
            case 'f': builder.Append('\f'); return true;
            case 'n': builder.Append('\n'); return true;
            case 'r': builder.Append('\r'); return true;
            case 't': builder.Append('\t'); return true;
            case 'v': builder.Append('\v'); return true;
            case '\\': builder.Append('\\'); return true;
            case '\'':
                if (quote != '\'')
                {
                    return false;
                }

                builder.Append('\'');
                return true;
            case '"':
                if (quote != '"')
                {
                    return false;
                }

                builder.Append('"');
                return true;
            case 'x':
                return TryAppendHex(body, ref index, 2, false, quote, builder, pending);
            case 'u':
                return TryAppendHex(body, ref index, 4, true, quote, builder, pending);
            case 'U':
                return TryAppendHex(body, ref index, 8, true, quote, builder, pending);
            default:
                if (c >= '0' && c <= '7')
                {
                    index--;
                    return TryAppendOctal(body, ref index, quote, builder, pending);
                }

                return false;
        }
    }

    private static bool TryAppendHex(string body, ref int index, int digits, bool codePoint, char quote,
        StringBuilder builder, List<byte> pending)
    {
        if (index + digits > body.Length)
        {
            return false;
        }

        int value = 0;
        for (int i = 0; i < digits; i++)
        {
            int digit = HexDigit(body[index + i]);
            if (digit < 0)
            {
                return false;
            }

            value = (value * 16) + digit;
        }

        index += digits;
        if (!codePoint)
        {
            AppendByte(value, quote, builder, pending);
            return true;
        }

        if (value > 0x10FFFF || (value >= 0xD800 && value <= 0xDFFF))
        {
            return false;
        }

        builder.Append(char.ConvertFromUtf32(value));
        return true;
    }

    private static void AppendByte(int value, char quote, StringBuilder builder, List<byte> pending)
    {
        if (quote == '\'')
        {
            //inside a character constant Go reads the escape as the rune with that value, not as a byte
            builder.Append((char)value);
            return;
        }

        pending.Add((byte)value);
    }

    private static bool TryAppendOctal(string body, ref int index, char quote, StringBuilder builder,
        List<byte> pending)
    {
        if (index + 3 > body.Length)
        {
            return false;
        }

        int value = 0;
        for (int i = 0; i < 3; i++)
        {
            char c = body[index + i];
            if (c < '0' || c > '7')
            {
                return false;
            }

            value = (value * 8) + (c - '0');
        }

        index += 3;
        if (value > 255)
        {
            return false;
        }

        AppendByte(value, quote, builder, pending);
        return true;
    }

    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'a' && c <= 'f')
        {
            return (c - 'a') + 10;
        }

        if (c >= 'A' && c <= 'F')
        {
            return (c - 'A') + 10;
        }

        return -1;
    }

    private static void AppendEscaped(StringBuilder builder, string value, char quote)
    {
        int i = 0;
        while (i < value.Length)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                AppendEscapedRune(builder, char.ConvertToUtf32(c, value[i + 1]), quote);
                i += 2;
                continue;
            }

            AppendEscapedRune(builder, c, quote);
            i++;
        }
    }

    private static void AppendEscapedRune(StringBuilder builder, int codePoint, char quote)
    {
        if (codePoint == quote || codePoint == '\\')
        {
            builder.Append('\\');
            builder.Append((char)codePoint);
            return;
        }

        if (IsPrint(codePoint))
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
            return;
        }

        switch (codePoint)
        {
            case '\a': builder.Append("\\a"); return;
            case '\b': builder.Append("\\b"); return;
            case '\f': builder.Append("\\f"); return;
            case '\n': builder.Append("\\n"); return;
            case '\r': builder.Append("\\r"); return;
            case '\t': builder.Append("\\t"); return;
            case '\v': builder.Append("\\v"); return;
        }

        if (codePoint < 0x20 || codePoint == 0x7F)
        {
            builder.Append("\\x");
            builder.Append(codePoint.ToString("x2", CultureInfo.InvariantCulture));
            return;
        }

        int rune = IsValidRune(codePoint) ? codePoint : ReplacementRune;
        if (rune < 0x10000)
        {
            builder.Append("\\u");
            builder.Append(rune.ToString("x4", CultureInfo.InvariantCulture));
            return;
        }

        builder.Append("\\U");
        builder.Append(rune.ToString("x8", CultureInfo.InvariantCulture));
    }

    private static bool IsValidRune(int codePoint)
        => codePoint >= 0 && codePoint <= 0x10FFFF && (codePoint < 0xD800 || codePoint > 0xDFFF);

    /// <summary>
    /// Go's <c>strconv.IsPrint</c>: the ASCII space and the Unicode letter, mark, number, punctuation
    /// and symbol categories are printable, and everything else - the other spaces, the format and
    /// control characters, the surrogates and the unassigned code points - is escaped.
    /// </summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns><see langword="true"/> when Go would print the code point as itself.</returns>
    private static bool IsPrint(int codePoint)
    {
        if (codePoint <= 0xFF)
        {
            if (codePoint >= 0x20 && codePoint <= 0x7E)
            {
                return true;
            }

            //U+00A0 no-break space is a space, not a graphic, and U+00AD soft hyphen is a format character
            return codePoint >= 0xA1 && codePoint != 0xAD;
        }

        if (!IsValidRune(codePoint))
        {
            return false;
        }

        switch (CharUnicodeInfo.GetUnicodeCategory(codePoint))
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.OtherNumber:
            case UnicodeCategory.ConnectorPunctuation:
            case UnicodeCategory.DashPunctuation:
            case UnicodeCategory.OpenPunctuation:
            case UnicodeCategory.ClosePunctuation:
            case UnicodeCategory.InitialQuotePunctuation:
            case UnicodeCategory.FinalQuotePunctuation:
            case UnicodeCategory.OtherPunctuation:
            case UnicodeCategory.MathSymbol:
            case UnicodeCategory.CurrencySymbol:
            case UnicodeCategory.ModifierSymbol:
            case UnicodeCategory.OtherSymbol:
                return true;
            default:
                return false;
        }
    }
}
