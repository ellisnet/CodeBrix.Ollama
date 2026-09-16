// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/parse.go (BSD-3-Clause);

/// <summary>
/// The parser for one Go text/template, and the parse tree it produces. A template text may define
/// further named templates with <c>{{define}}</c> or <c>{{block}}</c>; those land in the same tree set.
/// </summary>
internal sealed class GoTemplateTree
{
    private const int MaxStackDepth = 10000;
    private const int MaxBranchDepth = 500;

    private readonly GoTemplateItem[] _token = new GoTemplateItem[3];
    private List<string> _vars;
    private GoTemplateLexer _lexer;
    private Dictionary<string, GoTemplateTree> _treeSet;
    private HashSet<string> _funcNames;
    private int _peekCount;
    private int _actionLine;
    private int _rangeDepth;
    private int _stackDepth;
    private int _branchDepth;
    private string _text;

    private GoTemplateTree(string name)
    {
        Name = name;
    }

    /// <summary>The name of the template this tree represents.</summary>
    internal string Name { get; private set; }

    /// <summary>The name of the top-level template during parsing, used in error messages.</summary>
    internal string ParseName { get; private set; }

    /// <summary>The top-level node of the tree.</summary>
    internal GoListNode Root { get; set; }

    /// <summary>
    /// Parses template text into a set of trees keyed by template name. The text's own tree is stored
    /// under <paramref name="name"/>; any <c>{{define}}</c> or <c>{{block}}</c> bodies get their own entries.
    /// </summary>
    /// <param name="name">The name to give the top-level template.</param>
    /// <param name="text">The template text.</param>
    /// <param name="funcNames">The names of the functions the template may call.</param>
    /// <returns>The parsed trees, keyed by template name.</returns>
    /// <exception cref="ChatTemplateException">The template text is not valid.</exception>
    internal static Dictionary<string, GoTemplateTree> Parse(string name, string text,
        HashSet<string> funcNames)
    {
        Dictionary<string, GoTemplateTree> treeSet = new Dictionary<string, GoTemplateTree>(StringComparer.Ordinal);
        GoTemplateTree tree = new GoTemplateTree(name) { _text = text };
        tree.ParseTemplate(text, treeSet, funcNames);
        return treeSet;
    }

    /// <summary>
    /// Wraps a hand-built node list as a tree, so it can be rendered without being parsed from text.
    /// </summary>
    /// <param name="name">The template name.</param>
    /// <param name="root">The root node.</param>
    /// <returns>A tree set holding the one tree.</returns>
    internal static Dictionary<string, GoTemplateTree> FromRoot(string name, GoListNode root)
    {
        GoTemplateTree tree = new GoTemplateTree(name) { ParseName = name, Root = root, _text = string.Empty };
        return new Dictionary<string, GoTemplateTree>(StringComparer.Ordinal) { { name, tree } };
    }

    private void ParseTemplate(string text, Dictionary<string, GoTemplateTree> treeSet,
        HashSet<string> funcNames)
    {
        ParseName = Name;
        GoTemplateLexer lexer = new GoTemplateLexer(text);
        StartParse(funcNames, lexer, treeSet);
        _text = text;
        ParseRoot();
        Add();
        StopParse();
    }

    private void StartParse(HashSet<string> funcNames, GoTemplateLexer lexer,
        Dictionary<string, GoTemplateTree> treeSet)
    {
        Root = null;
        _lexer = lexer;
        _vars = new List<string> { "$" };
        _funcNames = funcNames;
        _treeSet = treeSet;
        _stackDepth = 0;
        _branchDepth = 0;
        lexer.EmitComments = false;
        lexer.BreakAllowed = !HasFunction("break");
        lexer.ContinueAllowed = !HasFunction("continue");
    }

    private void StopParse()
    {
        _lexer = null;
        _vars = null;
        _treeSet = null;
    }

    private void Add()
    {
        if (!_treeSet.TryGetValue(Name, out GoTemplateTree existing) || IsEmptyTree(existing.Root))
        {
            _treeSet[Name] = this;
            return;
        }

        if (!IsEmptyTree(Root))
        {
            ErrorAt(string.Format(CultureInfo.InvariantCulture, "template: multiple definition of template {0}",
                GoQuote.Quote(Name)));
        }
    }

    private static bool IsEmptyTree(GoNode node)
    {
        switch (node)
        {
            case null:
                return true;
            case GoCommentNode _:
                return true;
            case GoListNode list:
                foreach (GoNode child in list.Nodes)
                {
                    if (!IsEmptyTree(child))
                    {
                        return false;
                    }
                }

                return true;
            case GoTextNode text:
                return text.Text.Trim(' ', '\t', '\r', '\n', '\v', '\f').Length == 0;
            default:
                return false;
        }
    }

    private GoTemplateItem Next()
    {
        if (_peekCount > 0)
        {
            _peekCount--;
        }
        else
        {
            _token[0] = _lexer.NextItem();
        }

        return _token[_peekCount];
    }

    private void Backup() => _peekCount++;

    private void Backup2(GoTemplateItem t1)
    {
        _token[1] = t1;
        _peekCount = 2;
    }

    private void Backup3(GoTemplateItem t2, GoTemplateItem t1)
    {
        _token[1] = t1;
        _token[2] = t2;
        _peekCount = 3;
    }

    private GoTemplateItem Peek()
    {
        if (_peekCount > 0)
        {
            return _token[_peekCount - 1];
        }

        _peekCount = 1;
        _token[0] = _lexer.NextItem();
        return _token[0];
    }

    private GoTemplateItem NextNonSpace()
    {
        GoTemplateItem token;
        do
        {
            token = Next();
        }
        while (token.Type == GoTemplateItemType.Space);

        return token;
    }

    private GoTemplateItem PeekNonSpace()
    {
        GoTemplateItem token = NextNonSpace();
        Backup();
        return token;
    }

    private void ErrorAt(string message)
    {
        Root = null;
        throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture, "template: {0}:{1}: {2}",
            ParseName, _token[0].Line, message));
    }

    private GoTemplateItem Expect(GoTemplateItemType expected, string context)
    {
        GoTemplateItem token = NextNonSpace();
        if (token.Type != expected)
        {
            Unexpected(token, context);
        }

        return token;
    }

    private GoTemplateItem ExpectOneOf(GoTemplateItemType expected1, GoTemplateItemType expected2, string context)
    {
        GoTemplateItem token = NextNonSpace();
        if (token.Type != expected1 && token.Type != expected2)
        {
            Unexpected(token, context);
        }

        return token;
    }

    private void Unexpected(GoTemplateItem token, string context)
    {
        if (token.Type == GoTemplateItemType.Error)
        {
            string extra = string.Empty;
            if (_actionLine != 0 && _actionLine != token.Line)
            {
                extra = string.Format(CultureInfo.InvariantCulture, " in action started at {0}:{1}", ParseName,
                    _actionLine);
                if (token.Value != null && token.Value.EndsWith(" action", StringComparison.Ordinal))
                {
                    extra = extra.Substring(" in action".Length);
                }
            }

            ErrorAt(token + extra);
        }

        ErrorAt(string.Format(CultureInfo.InvariantCulture, "unexpected {0} in {1}", token, context));
    }

    private void ParseRoot()
    {
        Root = new GoListNode(Peek().Position);
        while (Peek().Type != GoTemplateItemType.Eof)
        {
            if (Peek().Type == GoTemplateItemType.LeftDelim)
            {
                GoTemplateItem delim = Next();
                if (NextNonSpace().Type == GoTemplateItemType.Define)
                {
                    GoTemplateTree definition = new GoTemplateTree("definition") { _text = _text, ParseName = ParseName };
                    definition.StartParse(_funcNames, _lexer, _treeSet);
                    definition.ParseDefinition();
                    continue;
                }

                Backup2(delim);
            }

            GoNode node = TextOrAction();
            if (node.NodeType == GoNodeType.End || node.NodeType == GoNodeType.Else)
            {
                ErrorAt("unexpected " + node);
            }

            Root.Append(node);
        }
    }

    private void ParseDefinition()
    {
        const string context = "define clause";
        GoTemplateItem name = ExpectOneOf(GoTemplateItemType.String, GoTemplateItemType.RawString, context);
        if (!GoQuote.TryUnquote(name.Value, out string unquoted))
        {
            ErrorAt("invalid syntax in " + context);
        }

        Name = unquoted;
        Expect(GoTemplateItemType.RightDelim, context);
        GoNode end = ItemList(out GoListNode list);
        Root = list;
        if (end.NodeType != GoNodeType.End)
        {
            ErrorAt(string.Format(CultureInfo.InvariantCulture, "unexpected {0} in {1}", end, context));
        }

        Add();
        StopParse();
    }

    private GoNode ItemList(out GoListNode list)
    {
        list = new GoListNode(PeekNonSpace().Position);
        while (PeekNonSpace().Type != GoTemplateItemType.Eof)
        {
            GoNode node = TextOrAction();
            if (node.NodeType == GoNodeType.End || node.NodeType == GoNodeType.Else)
            {
                return node;
            }

            list.Append(node);
        }

        ErrorAt("unexpected EOF");
        return null;
    }

    private GoNode TextOrAction()
    {
        GoTemplateItem token = NextNonSpace();
        switch (token.Type)
        {
            case GoTemplateItemType.Text:
                return new GoTextNode(token.Position, token.Value);
            case GoTemplateItemType.LeftDelim:
                _actionLine = token.Line;
                try
                {
                    return Action();
                }
                finally
                {
                    _actionLine = 0;
                }

            case GoTemplateItemType.Comment:
                return new GoCommentNode(token.Position, token.Value);
            default:
                Unexpected(token, "input");
                return null;
        }
    }

    private GoNode Action()
    {
        GoTemplateItem token = NextNonSpace();
        switch (token.Type)
        {
            case GoTemplateItemType.Block:
                return BlockControl();
            case GoTemplateItemType.Break:
                return BreakControl(token.Position, token.Line, GoNodeType.Break);
            case GoTemplateItemType.Continue:
                return BreakControl(token.Position, token.Line, GoNodeType.Continue);
            case GoTemplateItemType.Else:
                return ElseControl();
            case GoTemplateItemType.End:
                return new GoMarkerNode(GoNodeType.End,
                    Expect(GoTemplateItemType.RightDelim, "end").Position, token.Line);
            case GoTemplateItemType.If:
                return BranchControl(GoNodeType.If);
            case GoTemplateItemType.Range:
                return BranchControl(GoNodeType.Range);
            case GoTemplateItemType.Template:
                return TemplateControl();
            case GoTemplateItemType.With:
                return BranchControl(GoNodeType.With);
        }

        Backup();
        GoTemplateItem peeked = Peek();
        return new GoActionNode(peeked.Position, peeked.Line, Pipeline("command", GoTemplateItemType.RightDelim));
    }

    private GoNode BreakControl(int position, int line, GoNodeType type)
    {
        GoTemplateItem token = NextNonSpace();
        string what = type == GoNodeType.Break ? "{{break}}" : "{{continue}}";
        if (token.Type != GoTemplateItemType.RightDelim)
        {
            Unexpected(token, what);
        }

        if (_rangeDepth == 0)
        {
            ErrorAt(what + " outside {{range}}");
        }

        return new GoJumpNode(type, position, line);
    }

    private GoPipeNode Pipeline(string context, GoTemplateItemType end)
    {
        GoTemplateItem start = PeekNonSpace();
        GoPipeNode pipe = new GoPipeNode(start.Position, start.Line);
        while (true)
        {
            GoTemplateItem v = PeekNonSpace();
            if (v.Type != GoTemplateItemType.Variable)
            {
                break;
            }

            Next();
            //in "$x foo" the third token is needed to tell an argument variable from a declaration
            GoTemplateItem tokenAfterVariable = Peek();
            GoTemplateItem next = PeekNonSpace();
            if (next.Type == GoTemplateItemType.Assign || next.Type == GoTemplateItemType.Declare)
            {
                pipe.IsAssign = next.Type == GoTemplateItemType.Assign;
                NextNonSpace();
                pipe.Decl.Add(new GoVariableNode(v.Position, v.Value));
                _vars.Add(v.Value);
                break;
            }

            if (next.Type == GoTemplateItemType.Char && next.Value == ",")
            {
                NextNonSpace();
                pipe.Decl.Add(new GoVariableNode(v.Position, v.Value));
                _vars.Add(v.Value);
                if (context == "range" && pipe.Decl.Count < 2)
                {
                    GoTemplateItemType following = PeekNonSpace().Type;
                    if (following == GoTemplateItemType.Variable || following == GoTemplateItemType.RightDelim
                        || following == GoTemplateItemType.RightParen)
                    {
                        continue; //a second initialized variable in a range pipeline
                    }

                    ErrorAt("range can only initialize variables");
                }

                ErrorAt("too many declarations in " + context);
            }

            if (tokenAfterVariable.Type == GoTemplateItemType.Space)
            {
                Backup3(v, tokenAfterVariable);
            }
            else
            {
                Backup2(v);
            }

            break;
        }

        while (true)
        {
            GoTemplateItem token = NextNonSpace();
            if (token.Type == end)
            {
                CheckPipeline(pipe, context);
                return pipe;
            }

            switch (token.Type)
            {
                case GoTemplateItemType.Bool:
                case GoTemplateItemType.CharConstant:
                case GoTemplateItemType.Complex:
                case GoTemplateItemType.Dot:
                case GoTemplateItemType.Field:
                case GoTemplateItemType.Identifier:
                case GoTemplateItemType.Number:
                case GoTemplateItemType.Nil:
                case GoTemplateItemType.RawString:
                case GoTemplateItemType.String:
                case GoTemplateItemType.Variable:
                case GoTemplateItemType.LeftParen:
                    Backup();
                    pipe.Append(Command());
                    break;
                default:
                    Unexpected(token, context);
                    break;
            }
        }
    }

    private void CheckPipeline(GoPipeNode pipe, string context)
    {
        if (pipe.Cmds.Count == 0)
        {
            ErrorAt("missing value for " + context);
        }

        for (int i = 1; i < pipe.Cmds.Count; i++)
        {
            switch (pipe.Cmds[i].Args[0].NodeType)
            {
                case GoNodeType.Bool:
                case GoNodeType.Dot:
                case GoNodeType.Nil:
                case GoNodeType.Number:
                case GoNodeType.String:
                    ErrorAt(string.Format(CultureInfo.InvariantCulture,
                        "non executable command in pipeline stage {0}", i + 1));
                    break;
            }
        }
    }

    private GoNode BranchControl(GoNodeType type)
    {
        string context = type == GoNodeType.If ? "if" : type == GoNodeType.Range ? "range" : "with";
        if (_branchDepth >= MaxBranchDepth)
        {
            //Go grows a goroutine stack instead of stopping; we cap the recursion so that a pathological
            //template raises ChatTemplateException rather than overflowing the CLR stack
            ErrorAt("max nesting depth exceeded");
        }

        int varMark = _vars.Count;
        GoPipeNode pipe;
        GoListNode list;
        GoListNode elseList = null;
        _branchDepth++;
        try
        {
            pipe = Pipeline(context, GoTemplateItemType.RightDelim);
            if (type == GoNodeType.Range)
            {
                _rangeDepth++;
            }

            GoNode next = ItemList(out list);
            if (type == GoNodeType.Range)
            {
                _rangeDepth--;
            }

            if (next.NodeType == GoNodeType.Else)
            {
                //"{{else if}}" and "{{else with}}" are parsed as a nested branch whose {{end}} is shared
                if (type == GoNodeType.If && Peek().Type == GoTemplateItemType.If)
                {
                    Next();
                    elseList = new GoListNode(next.Position);
                    elseList.Append(BranchControl(GoNodeType.If));
                }
                else if (type == GoNodeType.With && Peek().Type == GoTemplateItemType.With)
                {
                    Next();
                    elseList = new GoListNode(next.Position);
                    elseList.Append(BranchControl(GoNodeType.With));
                }
                else
                {
                    next = ItemList(out elseList);
                    if (next.NodeType != GoNodeType.End)
                    {
                        ErrorAt("expected end; found " + next);
                    }
                }
            }
        }
        finally
        {
            _branchDepth--;
            if (_vars != null && _vars.Count > varMark)
            {
                _vars.RemoveRange(varMark, _vars.Count - varMark);
            }
        }

        return new GoBranchNode(type, pipe.Position, pipe.Line, pipe, list, elseList);
    }

    private GoNode ElseControl()
    {
        GoTemplateItem peek = PeekNonSpace();
        //"{{else if ..." and "{{else with ..." are handled as "{{else}}{{if ..." by the caller
        if (peek.Type == GoTemplateItemType.If || peek.Type == GoTemplateItemType.With)
        {
            return new GoMarkerNode(GoNodeType.Else, peek.Position, peek.Line);
        }

        GoTemplateItem token = Expect(GoTemplateItemType.RightDelim, "else");
        return new GoMarkerNode(GoNodeType.Else, token.Position, token.Line);
    }

    private GoNode BlockControl()
    {
        const string context = "block clause";
        GoTemplateItem token = NextNonSpace();
        string name = ParseTemplateName(token, context);
        GoPipeNode pipe = Pipeline(context, GoTemplateItemType.RightDelim);

        GoTemplateTree block = new GoTemplateTree(name) { _text = _text, ParseName = ParseName };
        block.StartParse(_funcNames, _lexer, _treeSet);
        GoNode end = block.ItemList(out GoListNode body);
        block.Root = body;
        if (end.NodeType != GoNodeType.End)
        {
            ErrorAt(string.Format(CultureInfo.InvariantCulture, "unexpected {0} in {1}", end, context));
        }

        block.Add();
        block.StopParse();
        return new GoTemplateCallNode(token.Position, token.Line, name, pipe);
    }

    private GoNode TemplateControl()
    {
        const string context = "template clause";
        GoTemplateItem token = NextNonSpace();
        string name = ParseTemplateName(token, context);
        GoPipeNode pipe = null;
        if (NextNonSpace().Type != GoTemplateItemType.RightDelim)
        {
            Backup();
            pipe = Pipeline(context, GoTemplateItemType.RightDelim);
        }

        return new GoTemplateCallNode(token.Position, token.Line, name, pipe);
    }

    private string ParseTemplateName(GoTemplateItem token, string context)
    {
        if (token.Type != GoTemplateItemType.String && token.Type != GoTemplateItemType.RawString)
        {
            Unexpected(token, context);
        }

        if (!GoQuote.TryUnquote(token.Value, out string name))
        {
            ErrorAt("invalid syntax in " + context);
        }

        return name;
    }

    private GoCommandNode Command()
    {
        GoCommandNode cmd = new GoCommandNode(PeekNonSpace().Position);
        while (true)
        {
            PeekNonSpace(); //skip leading spaces
            GoNode operand = Operand();
            if (operand != null)
            {
                cmd.Append(operand);
            }

            GoTemplateItem token = Next();
            if (token.Type == GoTemplateItemType.Space)
            {
                continue;
            }

            if (token.Type == GoTemplateItemType.RightDelim || token.Type == GoTemplateItemType.RightParen)
            {
                Backup();
            }
            else if (token.Type != GoTemplateItemType.Pipe)
            {
                Unexpected(token, "operand");
            }

            break;
        }

        if (cmd.Args.Count == 0)
        {
            ErrorAt("empty command");
        }

        return cmd;
    }

    private GoNode Operand()
    {
        GoNode node = Term();
        if (node == null)
        {
            return null;
        }

        if (Peek().Type == GoTemplateItemType.Field)
        {
            GoChainNode chain = new GoChainNode(Peek().Position, node);
            while (Peek().Type == GoTemplateItemType.Field)
            {
                chain.Add(Next().Value);
            }

            //for compatibility with the original API, a field or variable term absorbs the extra fields
            switch (node.NodeType)
            {
                case GoNodeType.Field:
                    node = new GoFieldNode(chain.Position, chain.ToString());
                    break;
                case GoNodeType.Variable:
                    node = new GoVariableNode(chain.Position, chain.ToString());
                    break;
                case GoNodeType.Bool:
                case GoNodeType.String:
                case GoNodeType.Number:
                case GoNodeType.Nil:
                case GoNodeType.Dot:
                    ErrorAt(string.Format(CultureInfo.InvariantCulture, "unexpected . after term {0}",
                        GoQuote.Quote(node.ToString())));
                    break;
                default:
                    node = chain;
                    break;
            }
        }

        return node;
    }

    private GoNode Term()
    {
        GoTemplateItem token = NextNonSpace();
        switch (token.Type)
        {
            case GoTemplateItemType.Identifier:
                if (!HasFunction(token.Value))
                {
                    ErrorAt(string.Format(CultureInfo.InvariantCulture, "function {0} not defined",
                        GoQuote.Quote(token.Value)));
                }

                return new GoIdentifierNode(token.Position, token.Value);
            case GoTemplateItemType.Dot:
                return new GoDotNode(token.Position);
            case GoTemplateItemType.Nil:
                return new GoNilNode(token.Position);
            case GoTemplateItemType.Variable:
                return UseVar(token.Position, token.Value);
            case GoTemplateItemType.Field:
                return new GoFieldNode(token.Position, token.Value);
            case GoTemplateItemType.Bool:
                return new GoBoolNode(token.Position, token.Value == "true");
            case GoTemplateItemType.CharConstant:
            case GoTemplateItemType.Complex:
            case GoTemplateItemType.Number:
                return NewNumber(token);
            case GoTemplateItemType.LeftParen:
                if (_stackDepth >= MaxStackDepth)
                {
                    ErrorAt("max expression depth exceeded");
                }

                _stackDepth++;
                try
                {
                    return Pipeline("parenthesized pipeline", GoTemplateItemType.RightParen);
                }
                finally
                {
                    _stackDepth--;
                }

            case GoTemplateItemType.String:
            case GoTemplateItemType.RawString:
                if (!GoQuote.TryUnquote(token.Value, out string text))
                {
                    ErrorAt("invalid syntax for string constant");
                }

                return new GoStringNode(token.Position, token.Value, text);
        }

        Backup();
        return null;
    }

    private GoNode NewNumber(GoTemplateItem token)
    {
        GoNumberNode number = new GoNumberNode(token.Position, token.Value);
        if (token.Type == GoTemplateItemType.CharConstant)
        {
            if (!GoQuote.TryUnquote(token.Value, out string character) || character.Length == 0)
            {
                ErrorAt("malformed character constant: " + token.Value);
            }

            int codePoint = char.ConvertToUtf32(character, 0);
            number.IsInt = true;
            number.Int64 = codePoint;
            number.IsUint = true;
            number.Uint64 = (ulong)codePoint;
            number.IsFloat = true;
            number.Float64 = codePoint;
            return number;
        }

        if (token.Type == GoTemplateItemType.Complex)
        {
            ErrorAt("complex constants are not supported");
        }

        string raw = token.Value;
        if (raw.EndsWith("i", StringComparison.Ordinal))
        {
            ErrorAt("imaginary constants are not supported");
        }

        if (TryParseGoUint(raw, out ulong unsigned))
        {
            number.IsUint = true;
            number.Uint64 = unsigned;
        }

        if (TryParseGoInt(raw, out long signed))
        {
            number.IsInt = true;
            number.Int64 = signed;
            if (signed == 0)
            {
                number.IsUint = true;
                number.Uint64 = unsigned;
            }
        }

        if (number.IsInt)
        {
            number.IsFloat = true;
            number.Float64 = number.Int64;
        }
        else if (number.IsUint)
        {
            number.IsFloat = true;
            number.Float64 = number.Uint64;
        }
        else if (double.TryParse(raw.Replace("_", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture,
                     out double floating))
        {
            if (raw.IndexOfAny(new[] { '.', 'e', 'E', 'p', 'P' }) < 0)
            {
                ErrorAt(string.Format(CultureInfo.InvariantCulture, "integer overflow: {0}", GoQuote.Quote(raw)));
            }

            number.IsFloat = true;
            number.Float64 = floating;
            if (!number.IsInt && (double)(long)floating == floating)
            {
                number.IsInt = true;
                number.Int64 = (long)floating;
            }

            if (!number.IsUint && floating >= 0 && (double)(ulong)floating == floating)
            {
                number.IsUint = true;
                number.Uint64 = (ulong)floating;
            }
        }

        if (!number.IsInt && !number.IsUint && !number.IsFloat)
        {
            ErrorAt(string.Format(CultureInfo.InvariantCulture, "illegal number syntax: {0}", GoQuote.Quote(raw)));
        }

        return number;
    }

    private static bool TryParseGoUint(string text, out ulong value)
    {
        value = 0;
        if (!TrySplitGoInteger(text, out bool negative, out int radix, out string digits) || negative)
        {
            return false;
        }

        try
        {
            value = Convert.ToUInt64(digits, radix);
            return true;
        }
        catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseGoInt(string text, out long value)
    {
        value = 0;
        if (!TrySplitGoInteger(text, out bool negative, out int radix, out string digits))
        {
            return false;
        }

        try
        {
            ulong magnitude = Convert.ToUInt64(digits, radix);
            if (negative)
            {
                if (magnitude > 9223372036854775808UL)
                {
                    return false;
                }

                value = magnitude == 9223372036854775808UL ? long.MinValue : -(long)magnitude;
                return true;
            }

            if (magnitude > long.MaxValue)
            {
                return false;
            }

            value = (long)magnitude;
            return true;
        }
        catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
        {
            return false;
        }
    }

    private static bool TrySplitGoInteger(string text, out bool negative, out int radix, out string digits)
    {
        negative = false;
        radix = 10;
        digits = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string s = text.Replace("_", string.Empty);
        if (s.StartsWith("+", StringComparison.Ordinal))
        {
            s = s.Substring(1);
        }
        else if (s.StartsWith("-", StringComparison.Ordinal))
        {
            negative = true;
            s = s.Substring(1);
        }

        if (s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
        {
            radix = 16;
            s = s.Substring(2);
        }
        else if (s.Length > 2 && s[0] == '0' && (s[1] == 'o' || s[1] == 'O'))
        {
            radix = 8;
            s = s.Substring(2);
        }
        else if (s.Length > 2 && s[0] == '0' && (s[1] == 'b' || s[1] == 'B'))
        {
            radix = 2;
            s = s.Substring(2);
        }
        else if (s.Length > 1 && s[0] == '0')
        {
            radix = 8;
            s = s.Substring(1);
        }

        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            int digit = c >= '0' && c <= '9' ? c - '0'
                : c >= 'a' && c <= 'f' ? (c - 'a') + 10
                : c >= 'A' && c <= 'F' ? (c - 'A') + 10
                : -1;
            if (digit < 0 || digit >= radix)
            {
                return false;
            }
        }

        digits = s;
        return true;
    }

    private bool HasFunction(string name) => _funcNames != null && _funcNames.Contains(name);

    private GoNode UseVar(int position, string name)
    {
        GoVariableNode variable = new GoVariableNode(position, name);
        foreach (string varName in _vars)
        {
            if (varName == variable.Ident[0])
            {
                return variable;
            }
        }

        ErrorAt(string.Format(CultureInfo.InvariantCulture, "undefined variable {0}",
            GoQuote.Quote(variable.Ident[0])));
        return null;
    }
}
