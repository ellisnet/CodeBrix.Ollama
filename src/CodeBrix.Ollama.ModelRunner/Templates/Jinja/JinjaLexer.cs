using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Turns Jinja template source into a token stream. The environment settings are fixed to the ones
/// the Hugging Face transformers library renders chat templates with - <c>trim_blocks</c> and
/// <c>lstrip_blocks</c> on, <c>keep_trailing_newline</c> off - and the explicit <c>-</c> and <c>+</c>
/// whitespace-control markers override them per tag.
/// </summary>
internal sealed class JinjaLexer
{
    private static readonly char[] WhitespaceCharacters = { ' ', '\t', '\r', '\n', '\f', '\v' };

    private readonly string _source;

    private readonly List<JinjaToken> _tokens = new List<JinjaToken>();

    private bool _stripLeadingWhitespace;

    private bool _trimLeadingNewline;

    private bool _atLineStart = true;

    private JinjaLexer(string source)
    {
        _source = source;
    }

    /// <summary>
    /// Removes the single trailing newline that Jinja drops when <c>keep_trailing_newline</c> is off.
    /// </summary>
    /// <param name="source">The raw template source.</param>
    /// <returns>The source the lexer works on.</returns>
    internal static string PrepareSource(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        if (source.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return source.Substring(0, source.Length - 2);
        }

        if (source[source.Length - 1] == '\n')
        {
            return source.Substring(0, source.Length - 1);
        }

        return source;
    }

    /// <summary>Tokenizes a prepared template source.</summary>
    /// <param name="source">The source returned by <see cref="PrepareSource"/>.</param>
    /// <returns>The tokens, ending with an <see cref="JinjaTokenKind.EndOfFile"/> token.</returns>
    /// <exception cref="ChatTemplateException">The source contains an unclosed tag or literal.</exception>
    internal static IReadOnlyList<JinjaToken> Tokenize(string source)
    {
        var lexer = new JinjaLexer(source ?? string.Empty);
        lexer.Run();
        return lexer._tokens;
    }

    /// <summary>Converts a source index into a one-based line and column.</summary>
    /// <param name="source">The source the index refers to.</param>
    /// <param name="index">The zero-based index.</param>
    /// <param name="line">Receives the one-based line number.</param>
    /// <param name="column">Receives the one-based column number.</param>
    internal static void GetLineColumn(string source, int index, out int line, out int column)
    {
        line = 1;
        column = 1;
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        int limit = Math.Min(index, source.Length);
        for (int i = 0; i < limit; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
    }

    /// <summary>Builds the exception used for every lexer and parser error.</summary>
    /// <param name="source">The source being parsed.</param>
    /// <param name="index">The zero-based index the error was found at.</param>
    /// <param name="message">What went wrong.</param>
    /// <returns>The exception to throw.</returns>
    internal static ChatTemplateException SyntaxError(string source, int index, string message)
    {
        GetLineColumn(source, index, out int line, out int column);
        return new ChatTemplateException(string.Format(
            CultureInfo.InvariantCulture,
            "Jinja template syntax error at line {0}, column {1}: {2}",
            line,
            column,
            message));
    }

    private void Run()
    {
        int position = 0;
        int textStart = 0;

        while (true)
        {
            int open = -1;
            char kind = '\0';
            for (int i = position; i + 1 < _source.Length; i++)
            {
                if (_source[i] != '{')
                {
                    continue;
                }

                char following = _source[i + 1];
                if (following == '{' || following == '%' || following == '#')
                {
                    open = i;
                    kind = following;
                    break;
                }
            }

            if (open < 0)
            {
                EmitText(_source.Substring(textStart), false, false, false);
                break;
            }

            int inner = open + 2;
            bool trimBefore = false;
            bool plusBefore = false;
            if (inner < _source.Length && _source[inner] == '-')
            {
                trimBefore = true;
                inner++;
            }
            else if (inner < _source.Length && _source[inner] == '+')
            {
                plusBefore = true;
                inner++;
            }

            bool isBlockTag = kind != '{';
            EmitText(_source.Substring(textStart, open - textStart), trimBefore, isBlockTag, plusBefore);

            if (kind == '#')
            {
                int end = _source.IndexOf("#}", inner, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw SyntaxError(_source, open, "unclosed comment; expected '#}'");
                }

                bool commentTrimAfter = end > inner && _source[end - 1] == '-';
                bool commentPlusAfter = end > inner && _source[end - 1] == '+';
                SetTrailingRules(commentTrimAfter, commentPlusAfter, true);
                position = end + 2;
                textStart = position;
                continue;
            }

            if (kind == '%')
            {
                int scan = inner;
                while (scan < _source.Length && IsInlineSpace(_source[scan]))
                {
                    scan++;
                }

                int nameStart = scan;
                while (scan < _source.Length && IsNameCharacter(_source[scan]))
                {
                    scan++;
                }

                string firstName = _source.Substring(nameStart, scan - nameStart);
                if (firstName == "raw" || firstName == "verbatim")
                {
                    position = ReadRawBlock(open, scan, firstName);
                    textStart = position;
                    continue;
                }
            }

            bool isVariable = kind == '{';
            _tokens.Add(new JinjaToken(
                isVariable ? JinjaTokenKind.VariableStart : JinjaTokenKind.BlockStart, null, open));
            int after = ReadTagTokens(inner, isVariable, out bool trimAfter, out bool plusAfter);
            _tokens.Add(new JinjaToken(
                isVariable ? JinjaTokenKind.VariableEnd : JinjaTokenKind.BlockEnd,
                null,
                after - (trimAfter || plusAfter ? 3 : 2)));
            SetTrailingRules(trimAfter, plusAfter, isBlockTag);
            position = after;
            textStart = position;
        }

        _tokens.Add(new JinjaToken(JinjaTokenKind.EndOfFile, null, _source.Length));
    }

    private int ReadRawBlock(int open, int afterName, string keyword)
    {
        int cursor = afterName;
        while (cursor < _source.Length && IsInlineSpace(_source[cursor]))
        {
            cursor++;
        }

        bool openTrimAfter = cursor < _source.Length && _source[cursor] == '-';
        bool openPlusAfter = cursor < _source.Length && _source[cursor] == '+';
        if (openTrimAfter || openPlusAfter)
        {
            cursor++;
        }

        if (cursor + 1 >= _source.Length || _source[cursor] != '%' || _source[cursor + 1] != '}')
        {
            throw SyntaxError(_source, open, "unclosed '{% " + keyword + " %}' tag");
        }

        int bodyStart = cursor + 2;
        string endKeyword = keyword == "raw" ? "endraw" : "endverbatim";
        int search = bodyStart;
        while (true)
        {
            int candidate = _source.IndexOf("{%", search, StringComparison.Ordinal);
            if (candidate < 0)
            {
                throw SyntaxError(_source, open, "unclosed '{% " + keyword + " %}' block");
            }

            int probe = candidate + 2;
            bool endTrimBefore = probe < _source.Length && _source[probe] == '-';
            bool endPlusBefore = probe < _source.Length && _source[probe] == '+';
            if (endTrimBefore || endPlusBefore)
            {
                probe++;
            }

            while (probe < _source.Length && IsInlineSpace(_source[probe]))
            {
                probe++;
            }

            int nameStart = probe;
            while (probe < _source.Length && IsNameCharacter(_source[probe]))
            {
                probe++;
            }

            if (_source.Substring(nameStart, probe - nameStart) != endKeyword)
            {
                search = candidate + 2;
                continue;
            }

            while (probe < _source.Length && IsInlineSpace(_source[probe]))
            {
                probe++;
            }

            bool endTrimAfter = probe < _source.Length && _source[probe] == '-';
            bool endPlusAfter = probe < _source.Length && _source[probe] == '+';
            if (endTrimAfter || endPlusAfter)
            {
                probe++;
            }

            if (probe + 1 >= _source.Length || _source[probe] != '%' || _source[probe + 1] != '}')
            {
                throw SyntaxError(_source, candidate, "unclosed '{% " + endKeyword + " %}' tag");
            }

            SetTrailingRules(openTrimAfter, openPlusAfter, true);
            EmitText(_source.Substring(bodyStart, candidate - bodyStart), endTrimBefore, true, endPlusBefore);
            SetTrailingRules(endTrimAfter, endPlusAfter, true);
            return probe + 2;
        }
    }

    private void SetTrailingRules(bool trimAfter, bool plusAfter, bool isBlockTag)
    {
        _stripLeadingWhitespace = trimAfter;
        _trimLeadingNewline = !trimAfter && isBlockTag && !plusAfter;
    }

    private void EmitText(string text, bool trimBefore, bool isBlockTagAhead, bool plusBefore)
    {
        bool atLineStart = _atLineStart;
        if (_stripLeadingWhitespace)
        {
            string stripped = text.TrimStart(WhitespaceCharacters);
            atLineStart = text.IndexOf('\n', 0, text.Length - stripped.Length) >= 0;
            text = stripped;
        }
        else if (_trimLeadingNewline)
        {
            if (text.StartsWith("\r\n", StringComparison.Ordinal))
            {
                text = text.Substring(2);
                atLineStart = true;
            }
            else if (text.Length > 0 && text[0] == '\n')
            {
                text = text.Substring(1);
                atLineStart = true;
            }
        }

        _stripLeadingWhitespace = false;
        _trimLeadingNewline = false;
        _atLineStart = false;

        if (trimBefore)
        {
            text = text.TrimEnd(WhitespaceCharacters);
        }
        else if (isBlockTagAhead && !plusBefore)
        {
            text = StripLineIndent(text, atLineStart);
        }

        if (text.Length > 0)
        {
            _tokens.Add(new JinjaToken(JinjaTokenKind.Text, text, 0));
        }
    }

    private static string StripLineIndent(string text, bool atLineStart)
    {
        int start = text.LastIndexOf('\n') + 1;
        if (start == 0 && !atLineStart)
        {
            return text;
        }

        for (int i = start; i < text.Length; i++)
        {
            if (text[i] != ' ' && text[i] != '\t')
            {
                return text;
            }
        }

        return text.Substring(0, start);
    }

    private int ReadTagTokens(int position, bool isVariable, out bool trimAfter, out bool plusAfter)
    {
        trimAfter = false;
        plusAfter = false;
        int depth = 0;
        char closer = isVariable ? '}' : '%';

        while (true)
        {
            if (position >= _source.Length)
            {
                throw SyntaxError(_source, _source.Length, isVariable ? "unclosed '{{' tag" : "unclosed '{%' tag");
            }

            char current = _source[position];
            if (current == ' ' || current == '\t' || current == '\r' || current == '\n')
            {
                position++;
                continue;
            }

            if (depth == 0)
            {
                if ((current == '-' || current == '+')
                    && position + 2 < _source.Length
                    && _source[position + 1] == closer
                    && _source[position + 2] == '}')
                {
                    trimAfter = current == '-';
                    plusAfter = current == '+';
                    return position + 3;
                }

                if (current == closer && position + 1 < _source.Length && _source[position + 1] == '}')
                {
                    return position + 2;
                }
            }

            if (current == '"' || current == '\'')
            {
                position = ReadStringLiteral(position);
                continue;
            }

            if (char.IsDigit(current))
            {
                position = ReadNumberLiteral(position);
                continue;
            }

            if (IsNameStart(current))
            {
                int start = position;
                while (position < _source.Length && IsNameCharacter(_source[position]))
                {
                    position++;
                }

                _tokens.Add(new JinjaToken(JinjaTokenKind.Name, _source.Substring(start, position - start), start));
                continue;
            }

            if (current == '(' || current == '[' || current == '{')
            {
                depth++;
            }
            else if (current == ')' || current == ']' || current == '}')
            {
                depth--;
            }

            string two = position + 1 < _source.Length ? _source.Substring(position, 2) : string.Empty;
            if (two == "**" || two == "//" || two == "==" || two == "!=" || two == ">=" || two == "<=")
            {
                _tokens.Add(new JinjaToken(JinjaTokenKind.Operator, two, position));
                position += 2;
                continue;
            }

            if ("+-*/%~<>=|()[]{},:.!".IndexOf(current) < 0)
            {
                throw SyntaxError(_source, position, "unexpected character '" + current + "'");
            }

            _tokens.Add(new JinjaToken(JinjaTokenKind.Operator, current.ToString(), position));
            position++;
        }
    }

    private int ReadStringLiteral(int position)
    {
        int start = position;
        char quote = _source[position];
        position++;
        var builder = new StringBuilder();

        while (true)
        {
            if (position >= _source.Length)
            {
                throw SyntaxError(_source, start, "unterminated string literal");
            }

            char current = _source[position];
            if (current == quote)
            {
                position++;
                break;
            }

            if (current != '\\')
            {
                builder.Append(current);
                position++;
                continue;
            }

            position++;
            if (position >= _source.Length)
            {
                throw SyntaxError(_source, start, "unterminated string literal");
            }

            char escape = _source[position];
            position++;
            switch (escape)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '\'':
                    builder.Append('\'');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case 'a':
                    builder.Append('\a');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case '\n':
                    break;
                case 'x':
                    position = AppendHex(builder, position, 2, start);
                    break;
                case 'u':
                    position = AppendHex(builder, position, 4, start);
                    break;
                case 'U':
                    position = AppendHex(builder, position, 8, start);
                    break;
                default:
                    builder.Append('\\');
                    builder.Append(escape);
                    break;
            }
        }

        _tokens.Add(new JinjaToken(JinjaTokenKind.String, builder.ToString(), start));
        return position;
    }

    private int AppendHex(StringBuilder builder, int position, int digits, int literalStart)
    {
        if (position + digits > _source.Length)
        {
            throw SyntaxError(_source, literalStart, "truncated escape sequence in string literal");
        }

        string text = _source.Substring(position, digits);
        if (!long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value))
        {
            throw SyntaxError(_source, literalStart, "invalid escape sequence in string literal");
        }

        if (value < 0L || value > 0x10FFFFL)
        {
            throw SyntaxError(
                _source, literalStart, "escape sequence '\\U" + text + "' is outside the Unicode range");
        }

        if (value >= 0xD800L && value <= 0xDFFFL)
        {
            if (digits >= 8)
            {
                throw SyntaxError(
                    _source, literalStart, "escape sequence '\\U" + text + "' names a surrogate code point");
            }

            builder.Append((char)value);
            return position + digits;
        }

        builder.Append(char.ConvertFromUtf32((int)value));
        return position + digits;
    }

    private int ReadNumberLiteral(int position)
    {
        int start = position;
        bool isFloat = false;

        while (position < _source.Length && char.IsDigit(_source[position]))
        {
            position++;
        }

        if (position + 1 < _source.Length && _source[position] == '.' && char.IsDigit(_source[position + 1]))
        {
            isFloat = true;
            position++;
            while (position < _source.Length && char.IsDigit(_source[position]))
            {
                position++;
            }
        }

        if (position < _source.Length && (_source[position] == 'e' || _source[position] == 'E'))
        {
            int probe = position + 1;
            if (probe < _source.Length && (_source[probe] == '+' || _source[probe] == '-'))
            {
                probe++;
            }

            if (probe < _source.Length && char.IsDigit(_source[probe]))
            {
                isFloat = true;
                position = probe;
                while (position < _source.Length && char.IsDigit(_source[position]))
                {
                    position++;
                }
            }
        }

        string text = _source.Substring(start, position - start);
        if (isFloat)
        {
            _tokens.Add(new JinjaToken(
                JinjaTokenKind.Float, text, start, double.Parse(text, CultureInfo.InvariantCulture)));
        }
        else
        {
            _tokens.Add(new JinjaToken(
                JinjaTokenKind.Integer, text, start, long.Parse(text, CultureInfo.InvariantCulture)));
        }

        return position;
    }

    private static bool IsInlineSpace(char value) => value == ' ' || value == '\t';

    private static bool IsNameStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsNameCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}
