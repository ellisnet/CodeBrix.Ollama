using System;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/lex.go (BSD-3-Clause);

/// <summary>
/// The Go text/template scanner. It turns template text into the token stream
/// <see cref="GoTemplateTree"/> parses, and it is where the <c>{{-</c> and <c>-}}</c> trim markers are
/// applied - the parser never sees them.
/// </summary>
internal sealed class GoTemplateLexer
{
    private const string SpaceChars = " \t\r\n";
    private const char TrimMarker = '-';
    private const int TrimMarkerLength = 2; //the marker plus the space before or after it
    private const string LeftComment = "/*";
    private const string RightComment = "*/";
    private const int Eof = -1;

    private const int StateDone = 0;
    private const int StateText = 1;
    private const int StateLeftDelim = 2;
    private const int StateComment = 3;
    private const int StateRightDelim = 4;
    private const int StateInsideAction = 5;
    private const int StateSpace = 6;
    private const int StateIdentifier = 7;
    private const int StateField = 8;
    private const int StateVariable = 9;
    private const int StateChar = 10;
    private const int StateNumber = 11;
    private const int StateQuote = 12;
    private const int StateRawQuote = 13;

    private string _input;
    private readonly string _leftDelim;
    private readonly string _rightDelim;
    private int _pos;
    private int _start;
    private bool _atEof;
    private int _parenDepth;
    private int _line;
    private int _startLine;
    private GoTemplateItem _item;
    private bool _insideAction;

    /// <summary>Creates a scanner over template text using the default <c>{{</c> and <c>}}</c> delimiters.</summary>
    /// <param name="input">The template text.</param>
    /// <exception cref="ChatTemplateException">The text holds an unpaired UTF-16 surrogate, which is not
    /// text Go could have read.</exception>
    internal GoTemplateLexer(string input)
    {
        _input = input ?? string.Empty;
        RejectUnpairedSurrogates(_input);
        _leftDelim = "{{";
        _rightDelim = "}}";
        _line = 1;
        _startLine = 1;
    }

    /// <summary>Whether <c>break</c> is scanned as a keyword rather than an identifier.</summary>
    internal bool BreakAllowed { get; set; }

    /// <summary>Whether <c>continue</c> is scanned as a keyword rather than an identifier.</summary>
    internal bool ContinueAllowed { get; set; }

    /// <summary>Whether comments are emitted as tokens rather than discarded.</summary>
    internal bool EmitComments { get; set; }

    /// <summary>
    /// Scans and returns the next token. At the end of the input it returns
    /// <see cref="GoTemplateItemType.Eof"/> forever.
    /// </summary>
    /// <returns>The next token.</returns>
    internal GoTemplateItem NextItem()
    {
        _item = new GoTemplateItem(GoTemplateItemType.Eof, _pos, "EOF", _startLine);
        int state = _insideAction ? StateInsideAction : StateText;
        while (state != StateDone)
        {
            switch (state)
            {
                case StateText: state = LexText(); break;
                case StateLeftDelim: state = LexLeftDelim(); break;
                case StateComment: state = LexComment(); break;
                case StateRightDelim: state = LexRightDelim(); break;
                case StateInsideAction: state = LexInsideAction(); break;
                case StateSpace: state = LexSpace(); break;
                case StateIdentifier: state = LexIdentifier(); break;
                case StateField: state = LexFieldOrVariable(GoTemplateItemType.Field); break;
                case StateVariable: state = LexVariable(); break;
                case StateChar: state = LexChar(); break;
                case StateNumber: state = LexNumber(); break;
                case StateQuote: state = LexQuote(); break;
                case StateRawQuote: state = LexRawQuote(); break;
                default: state = StateDone; break;
            }
        }

        return _item;
    }

    private int Next()
    {
        if (_pos >= _input.Length)
        {
            _atEof = true;
            return Eof;
        }

        char c = _input[_pos];
        if (char.IsHighSurrogate(c) && _pos + 1 < _input.Length && char.IsLowSurrogate(_input[_pos + 1]))
        {
            int rune = char.ConvertToUtf32(c, _input[_pos + 1]);
            _pos += 2;
            return rune;
        }

        _pos++;
        if (c == '\n')
        {
            _line++;
        }

        return c;
    }

    private int Peek()
    {
        int r = Next();
        Backup();
        return r;
    }

    private void Backup()
    {
        if (_atEof || _pos <= 0)
        {
            return;
        }

        char c = _input[_pos - 1];
        if (char.IsLowSurrogate(c) && _pos >= 2 && char.IsHighSurrogate(_input[_pos - 2]))
        {
            _pos -= 2;
            return;
        }

        _pos--;
        if (c == '\n')
        {
            _line--;
        }
    }

    private GoTemplateItem ThisItem(GoTemplateItemType type)
    {
        GoTemplateItem item = new GoTemplateItem(type, _start, _input.Substring(_start, _pos - _start), _startLine);
        _start = _pos;
        _startLine = _line;
        return item;
    }

    private int Emit(GoTemplateItemType type) => EmitItem(ThisItem(type));

    private int EmitItem(GoTemplateItem item)
    {
        _item = item;
        return StateDone;
    }

    private void Ignore()
    {
        _line += CountNewLines(_input, _start, _pos);
        _start = _pos;
        _startLine = _line;
    }

    private bool Accept(string valid)
    {
        int r = Next();
        if (r >= 0 && r <= char.MaxValue && valid.IndexOf((char)r) >= 0)
        {
            return true;
        }

        Backup();
        return false;
    }

    private void AcceptRun(string valid)
    {
        while (true)
        {
            int r = Next();
            if (r < 0 || r > char.MaxValue || valid.IndexOf((char)r) < 0)
            {
                break;
            }
        }

        Backup();
    }

    private int ErrorAt(string message)
    {
        _item = new GoTemplateItem(GoTemplateItemType.Error, _start, message, _startLine);
        _start = 0;
        _pos = 0;
        _input = string.Empty;
        return StateDone;
    }

    private int LexText()
    {
        int x = _input.IndexOf(_leftDelim, _pos, StringComparison.Ordinal);
        if (x >= 0)
        {
            if (x > _pos)
            {
                _pos = x;
                int trimLength = 0;
                int delimEnd = _pos + _leftDelim.Length;
                if (HasLeftTrimMarker(_input, delimEnd))
                {
                    trimLength = RightTrimLength(_input, _start, _pos);
                }

                _pos -= trimLength;
                _line += CountNewLines(_input, _start, _pos);
                GoTemplateItem item = ThisItem(GoTemplateItemType.Text);
                _pos += trimLength;
                Ignore();
                if (item.Value.Length > 0)
                {
                    return EmitItem(item);
                }
            }

            return StateLeftDelim;
        }

        _pos = _input.Length;
        if (_pos > _start)
        {
            _line += CountNewLines(_input, _start, _pos);
            return Emit(GoTemplateItemType.Text);
        }

        return Emit(GoTemplateItemType.Eof);
    }

    private int LexLeftDelim()
    {
        _pos += _leftDelim.Length;
        bool trimSpace = HasLeftTrimMarker(_input, _pos);
        int afterMarker = trimSpace ? TrimMarkerLength : 0;
        if (StartsWith(_input, _pos + afterMarker, LeftComment))
        {
            _pos += afterMarker;
            Ignore();
            return StateComment;
        }

        GoTemplateItem item = ThisItem(GoTemplateItemType.LeftDelim);
        _insideAction = true;
        _pos += afterMarker;
        Ignore();
        _parenDepth = 0;
        return EmitItem(item);
    }

    private int LexComment()
    {
        _pos += LeftComment.Length;
        int x = _input.IndexOf(RightComment, _pos, StringComparison.Ordinal);
        if (x < 0)
        {
            return ErrorAt("unclosed comment");
        }

        _pos = x + RightComment.Length;
        bool delim = AtRightDelim(out bool trimSpace);
        if (!delim)
        {
            return ErrorAt("comment ends before closing delimiter");
        }

        _line += CountNewLines(_input, _start, _pos);
        GoTemplateItem item = ThisItem(GoTemplateItemType.Comment);
        if (trimSpace)
        {
            _pos += TrimMarkerLength;
        }

        _pos += _rightDelim.Length;
        if (trimSpace)
        {
            _pos += LeftTrimLength(_input, _pos);
        }

        Ignore();
        return EmitComments ? EmitItem(item) : StateText;
    }

    private int LexRightDelim()
    {
        AtRightDelim(out bool trimSpace);
        if (trimSpace)
        {
            _pos += TrimMarkerLength;
            Ignore();
        }

        _pos += _rightDelim.Length;
        GoTemplateItem item = ThisItem(GoTemplateItemType.RightDelim);
        if (trimSpace)
        {
            _pos += LeftTrimLength(_input, _pos);
            Ignore();
        }

        _insideAction = false;
        return EmitItem(item);
    }

    private int LexInsideAction()
    {
        if (AtRightDelim(out _))
        {
            if (_parenDepth == 0)
            {
                return StateRightDelim;
            }

            return ErrorAt("unclosed left paren");
        }

        int r = Next();
        if (r == Eof)
        {
            return ErrorAt("unclosed action");
        }

        if (IsSpace(r))
        {
            Backup(); //put the space back in case we have " -}}"
            return StateSpace;
        }

        switch (r)
        {
            case '=':
                return Emit(GoTemplateItemType.Assign);
            case ':':
                if (Next() != '=')
                {
                    return ErrorAt("expected :=");
                }

                return Emit(GoTemplateItemType.Declare);
            case '|':
                return Emit(GoTemplateItemType.Pipe);
            case '"':
                return StateQuote;
            case '`':
                return StateRawQuote;
            case '$':
                return StateVariable;
            case '\'':
                return StateChar;
            case '(':
                _parenDepth++;
                return Emit(GoTemplateItemType.LeftParen);
            case ')':
                _parenDepth--;
                if (_parenDepth < 0)
                {
                    return ErrorAt("unexpected right paren");
                }

                return Emit(GoTemplateItemType.RightParen);
        }

        if (r == '.')
        {
            //special look-ahead for ".field" so we do not break Backup()
            if (_pos < _input.Length)
            {
                char c = _input[_pos];
                if (c < '0' || c > '9')
                {
                    return StateField;
                }
            }

            Backup();
            return StateNumber;
        }

        if (r == '+' || r == '-' || (r >= '0' && r <= '9'))
        {
            Backup();
            return StateNumber;
        }

        if (IsAlphaNumeric(r))
        {
            Backup();
            return StateIdentifier;
        }

        if (r <= 0x7F && !char.IsControl((char)r))
        {
            return Emit(GoTemplateItemType.Char);
        }

        return ErrorAt(string.Format(CultureInfo.InvariantCulture,
            "unrecognized character in action: U+{0:X4} {1}", r, GoQuote.QuoteRune(r)));
    }

    private int LexSpace()
    {
        int numSpaces = 0;
        while (true)
        {
            int r = Peek();
            if (!IsSpace(r))
            {
                break;
            }

            Next();
            numSpaces++;
        }

        //be careful about a trim-marked closing delimiter, which has a minus after a space
        if (HasRightTrimMarker(_input, _pos - 1) && StartsWith(_input, (_pos - 1) + TrimMarkerLength, _rightDelim))
        {
            Backup(); //before the space
            if (numSpaces == 1)
            {
                return StateRightDelim; //on the delimiter, so go right to it
            }
        }

        return Emit(GoTemplateItemType.Space);
    }

    private int LexIdentifier()
    {
        while (true)
        {
            int r = Next();
            if (IsAlphaNumeric(r))
            {
                continue;
            }

            Backup();
            string word = _input.Substring(_start, _pos - _start);
            if (!AtTerminator())
            {
                return BadCharacter(r);
            }

            GoTemplateItemType keyword = GoTemplateKeywords.Lookup(word);
            if (keyword > GoTemplateItemType.Keyword)
            {
                if ((keyword == GoTemplateItemType.Break && !BreakAllowed)
                    || (keyword == GoTemplateItemType.Continue && !ContinueAllowed))
                {
                    return Emit(GoTemplateItemType.Identifier);
                }

                return Emit(keyword);
            }

            if (word.Length > 0 && word[0] == '.')
            {
                return Emit(GoTemplateItemType.Field);
            }

            if (word == "true" || word == "false")
            {
                return Emit(GoTemplateItemType.Bool);
            }

            return Emit(GoTemplateItemType.Identifier);
        }
    }

    private int LexVariable()
    {
        if (AtTerminator())
        {
            return Emit(GoTemplateItemType.Variable);
        }

        return LexFieldOrVariable(GoTemplateItemType.Variable);
    }

    private int LexFieldOrVariable(GoTemplateItemType type)
    {
        if (AtTerminator())
        {
            return type == GoTemplateItemType.Variable
                ? Emit(GoTemplateItemType.Variable)
                : Emit(GoTemplateItemType.Dot);
        }

        int r;
        while (true)
        {
            r = Next();
            if (!IsAlphaNumeric(r))
            {
                Backup();
                break;
            }
        }

        if (!AtTerminator())
        {
            return BadCharacter(r);
        }

        return Emit(type);
    }

    private bool AtTerminator()
    {
        int r = Peek();
        if (IsSpace(r))
        {
            return true;
        }

        switch (r)
        {
            case Eof:
            case '.':
            case ',':
            case '|':
            case ':':
            case ')':
            case '(':
                return true;
        }

        return StartsWith(_input, _pos, _rightDelim);
    }

    private int LexChar()
    {
        while (true)
        {
            int r = Next();
            if (r == '\\')
            {
                int escaped = Next();
                if (escaped != Eof && escaped != '\n')
                {
                    continue;
                }

                return ErrorAt("unterminated character constant");
            }

            if (r == Eof || r == '\n')
            {
                return ErrorAt("unterminated character constant");
            }

            if (r == '\'')
            {
                break;
            }
        }

        return Emit(GoTemplateItemType.CharConstant);
    }

    private int LexNumber()
    {
        if (!ScanNumber())
        {
            return ErrorAt(string.Format(CultureInfo.InvariantCulture, "bad number syntax: {0}",
                GoQuote.Quote(_input.Substring(_start, _pos - _start))));
        }

        int sign = Peek();
        if (sign == '+' || sign == '-')
        {
            if (!ScanNumber() || _input[_pos - 1] != 'i')
            {
                return ErrorAt(string.Format(CultureInfo.InvariantCulture, "bad number syntax: {0}",
                    GoQuote.Quote(_input.Substring(_start, _pos - _start))));
            }

            return Emit(GoTemplateItemType.Complex);
        }

        return Emit(GoTemplateItemType.Number);
    }

    private bool ScanNumber()
    {
        Accept("+-");
        string digits = "0123456789_";
        if (Accept("0"))
        {
            if (Accept("xX"))
            {
                digits = "0123456789abcdefABCDEF_";
            }
            else if (Accept("oO"))
            {
                digits = "01234567_";
            }
            else if (Accept("bB"))
            {
                digits = "01_";
            }
        }

        AcceptRun(digits);
        if (Accept("."))
        {
            AcceptRun(digits);
        }

        if (digits.Length == 11 && Accept("eE"))
        {
            Accept("+-");
            AcceptRun("0123456789_");
        }

        if (digits.Length == 23 && Accept("pP"))
        {
            Accept("+-");
            AcceptRun("0123456789_");
        }

        Accept("i");
        if (IsAlphaNumeric(Peek()))
        {
            Next();
            return false;
        }

        return true;
    }

    private int LexQuote()
    {
        while (true)
        {
            int r = Next();
            if (r == '\\')
            {
                int escaped = Next();
                if (escaped != Eof && escaped != '\n')
                {
                    continue;
                }

                return ErrorAt("unterminated quoted string");
            }

            if (r == Eof || r == '\n')
            {
                return ErrorAt("unterminated quoted string");
            }

            if (r == '"')
            {
                break;
            }
        }

        return Emit(GoTemplateItemType.String);
    }

    private int LexRawQuote()
    {
        while (true)
        {
            int r = Next();
            if (r == Eof)
            {
                return ErrorAt("unterminated raw quoted string");
            }

            if (r == '`')
            {
                break;
            }
        }

        return Emit(GoTemplateItemType.RawString);
    }

    private int BadCharacter(int r)
    {
        string rendered = r == Eof
            ? "U+0000 '\\x00'"
            : string.Format(CultureInfo.InvariantCulture, "U+{0:X4} {1}", r, GoQuote.QuoteRune(r));
        return ErrorAt("bad character " + rendered);
    }

    private bool AtRightDelim(out bool trimSpaces)
    {
        if (HasRightTrimMarker(_input, _pos) && StartsWith(_input, _pos + TrimMarkerLength, _rightDelim))
        {
            trimSpaces = true;
            return true;
        }

        trimSpaces = false;
        return StartsWith(_input, _pos, _rightDelim);
    }

    private static bool StartsWith(string s, int offset, string prefix)
    {
        if (offset < 0 || offset + prefix.Length > s.Length)
        {
            return false;
        }

        return string.CompareOrdinal(s, offset, prefix, 0, prefix.Length) == 0;
    }

    private static int RightTrimLength(string s, int start, int end)
    {
        int i = end;
        while (i > start && SpaceChars.IndexOf(s[i - 1]) >= 0)
        {
            i--;
        }

        return end - i;
    }

    private static int LeftTrimLength(string s, int offset)
    {
        int i = offset;
        while (i < s.Length && SpaceChars.IndexOf(s[i]) >= 0)
        {
            i++;
        }

        return i - offset;
    }

    private static int CountNewLines(string s, int start, int end)
    {
        int count = 0;
        for (int i = start; i < end; i++)
        {
            if (s[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static void RejectUnpairedSurrogates(string input)
    {
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (!char.IsSurrogate(c))
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
            {
                i++;
                continue;
            }

            //Go reads a template as UTF-8 bytes, so text like this cannot reach its scanner at all; we
            //refuse it here rather than let it turn into replacement characters on the way out
            throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                "template: unpaired UTF-16 surrogate U+{0:X4} at offset {1}", (int)c, i));
        }
    }

    private static bool IsSpace(int r) => r == ' ' || r == '\t' || r == '\r' || r == '\n';

    private static bool IsAlphaNumeric(int r)
    {
        if (r < 0)
        {
            return false;
        }

        if (r == '_')
        {
            return true;
        }

        //Go asks unicode.IsLetter / unicode.IsDigit about the rune, so an astral code point only counts
        //when it really is a letter or a decimal digit - an emoji is neither
        if (!Rune.IsValid(r))
        {
            return false;
        }

        Rune rune = new Rune(r);
        return Rune.IsLetter(rune) || Rune.IsDigit(rune);
    }

    private static bool HasLeftTrimMarker(string s, int offset)
        => offset >= 0 && offset + 2 <= s.Length && s[offset] == TrimMarker && IsSpace(s[offset + 1]);

    private static bool HasRightTrimMarker(string s, int offset)
        => offset >= 0 && offset + 2 <= s.Length && IsSpace(s[offset]) && s[offset + 1] == TrimMarker;
}
