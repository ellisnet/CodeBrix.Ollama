using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Builds the node tree for a Jinja template. The grammar follows Jinja's own precedence, tightest
/// first: postfix access, then filters and tests, then unary <c>+</c>/<c>-</c>, <c>**</c>,
/// <c>* / // %</c>, the string concatenation <c>~</c>, <c>+ -</c>, the comparisons (including
/// <c>in</c>), <c>not</c>, <c>and</c>, <c>or</c> and finally the inline conditional. Note that
/// <c>~</c> binds TIGHTER than <c>+</c> and <c>-</c>, exactly as it does in Jinja, so
/// <c>1 + 2 ~ 3</c> is <c>1 + (2 ~ 3)</c>.
/// </summary>
internal sealed class JinjaParser
{
    private static readonly string[] NoTerminators = new string[0];

    private static readonly string[] TestArgumentStoppers =
    {
        "and", "or", "if", "else", "is", "not", "in",
    };

    private readonly string _source;

    private readonly IReadOnlyList<JinjaToken> _tokens;

    private readonly List<HashSet<string>> _macroNames = new List<HashSet<string>>();

    private int _index;

    /// <summary>
    /// How deeply the template may nest - statements, parentheses and operator, filter and attribute chains
    /// all counted together - before the parser refuses it. The tree is walked recursively both here and
    /// when it renders, so this is what keeps both inside the stack.
    /// </summary>
    private readonly int _maximumDepth;

    private int _depth;

    private JinjaParser(string source, IReadOnlyList<JinjaToken> tokens, int maximumDepth)
    {
        _source = source;
        _tokens = tokens;
        _maximumDepth = maximumDepth;
    }

    /// <summary>Parses a template body under the default nesting limit.</summary>
    /// <param name="source">The raw template source.</param>
    /// <returns>The top-level nodes.</returns>
    /// <exception cref="ChatTemplateException">The template is not valid Jinja.</exception>
    internal static IList<JinjaNode> Parse(string source)
        => Parse(source, TemplateLimits.Default.MaxNestingDepth);

    /// <summary>Parses a template body.</summary>
    /// <param name="source">The raw template source.</param>
    /// <param name="maximumDepth">How deeply the template may nest.</param>
    /// <returns>The top-level nodes.</returns>
    /// <exception cref="ChatTemplateException">The template is not valid Jinja, or nests too deeply.</exception>
    internal static IList<JinjaNode> Parse(string source, int maximumDepth)
    {
        string prepared = JinjaLexer.PrepareSource(source);
        var parser = new JinjaParser(prepared, JinjaLexer.Tokenize(prepared), maximumDepth);
        IList<JinjaNode> body = parser.ParseBody(NoTerminators, out string terminator);
        if (terminator != null)
        {
            throw parser.Error(parser.Current, "unexpected '" + terminator + "'");
        }

        return body;
    }

    /// <summary>Parses a standalone expression, used by the tests.</summary>
    /// <param name="source">The expression text.</param>
    /// <returns>The expression node.</returns>
    /// <exception cref="ChatTemplateException">The text is not a valid expression.</exception>
    internal static JinjaExpression ParseExpressionText(string source)
    {
        IList<JinjaNode> body = Parse("{{ " + source + " }}");
        if (body.Count != 1 || !(body[0] is JinjaOutputNode output))
        {
            throw new ChatTemplateException("Jinja template syntax error: '" + source + "' is not a single expression.");
        }

        return output.Expression;
    }

    private JinjaToken Current => _tokens[_index];

    private JinjaToken Peek(int offset)
    {
        int target = _index + offset;
        return target < _tokens.Count ? _tokens[target] : _tokens[_tokens.Count - 1];
    }

    private JinjaToken Advance() => _tokens[_index++];

    private ChatTemplateException Error(JinjaToken token, string message)
    {
        return JinjaLexer.SyntaxError(_source, token == null ? _source.Length : token.Index, message);
    }

    private void ExpectOperator(string value)
    {
        if (!Current.IsOperator(value))
        {
            throw Error(Current, "expected '" + value + "' but found " + Current);
        }

        _index++;
    }

    private void ExpectKind(JinjaTokenKind kind, string description)
    {
        if (Current.Kind != kind)
        {
            throw Error(Current, "expected " + description + " but found " + Current);
        }

        _index++;
    }

    private string ExpectName()
    {
        if (Current.Kind != JinjaTokenKind.Name)
        {
            throw Error(Current, "expected a name but found " + Current);
        }

        return Advance().Value;
    }

    private IList<JinjaNode> ParseBody(string[] terminators, out string terminator)
    {
        var nodes = new List<JinjaNode>();
        terminator = null;

        while (true)
        {
            JinjaToken token = Current;
            if (token.Kind == JinjaTokenKind.EndOfFile)
            {
                if (terminators.Length > 0)
                {
                    throw Error(token, "unexpected end of template; expected '" + terminators[0] + "'");
                }

                return nodes;
            }

            if (token.Kind == JinjaTokenKind.Text)
            {
                nodes.Add(new JinjaTextNode(token.Value));
                _index++;
                continue;
            }

            if (token.Kind == JinjaTokenKind.VariableStart)
            {
                _index++;
                JinjaExpression expression = ParseExpression(true);
                ExpectKind(JinjaTokenKind.VariableEnd, "'}}'");
                nodes.Add(new JinjaOutputNode(expression));
                continue;
            }

            if (token.Kind != JinjaTokenKind.BlockStart)
            {
                throw Error(token, "unexpected " + token);
            }

            JinjaToken keyword = Peek(1);
            if (keyword.Kind == JinjaTokenKind.Name && Array.IndexOf(terminators, keyword.Value) >= 0)
            {
                terminator = keyword.Value;
                return nodes;
            }

            _index++;
            EnterDepth();
            try
            {
                nodes.Add(ParseStatement());
            }
            finally
            {
                _depth--;
            }
        }
    }

    private void ConsumeSimpleTerminator()
    {
        ExpectKind(JinjaTokenKind.BlockStart, "'{%'");
        _index++;
        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
    }

    private JinjaNode ParseStatement()
    {
        JinjaToken keyword = Current;
        if (keyword.Kind != JinjaTokenKind.Name)
        {
            throw Error(keyword, "expected a statement name but found " + keyword);
        }

        switch (keyword.Value)
        {
            case "if":
                return ParseIf();
            case "for":
                return ParseFor();
            case "set":
                return ParseSet();
            case "macro":
                return ParseMacro();
            case "call":
                return ParseCallBlock();
            case "filter":
                return ParseFilterBlock();
            case "generation":
                return ParseSimpleBlock("endgeneration", false);
            case "with":
                return ParseWith();
            case "break":
                _index++;
                ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
                return new JinjaLoopControlNode(true);
            case "continue":
                _index++;
                ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
                return new JinjaLoopControlNode(false);
            case "do":
                _index++;
                JinjaExpression expression = ParseExpression(true);
                ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
                return new JinjaExpressionStatementNode(expression);
            default:
                throw Error(keyword, "unknown statement '" + keyword.Value + "'");
        }
    }

    private JinjaNode ParseIf()
    {
        var branches = new List<JinjaIfBranch>();
        IList<JinjaNode> elseBody = null;
        string terminator;

        _index++;
        JinjaExpression condition = ParseExpression(true);
        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        IList<JinjaNode> body = ParseBody(new[] { "endif", "elif", "else" }, out terminator);
        branches.Add(new JinjaIfBranch(condition, body));

        while (terminator == "elif")
        {
            ExpectKind(JinjaTokenKind.BlockStart, "'{%'");
            _index++;
            condition = ParseExpression(true);
            ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
            body = ParseBody(new[] { "endif", "elif", "else" }, out terminator);
            branches.Add(new JinjaIfBranch(condition, body));
        }

        if (terminator == "else")
        {
            ConsumeSimpleTerminator();
            elseBody = ParseBody(new[] { "endif" }, out terminator);
        }

        ConsumeSimpleTerminator();
        return new JinjaIfNode(branches, elseBody);
    }

    private JinjaNode ParseFor()
    {
        _index++;
        var targets = new List<string> { ExpectName() };
        while (Current.IsOperator(","))
        {
            _index++;
            targets.Add(ExpectName());
        }

        if (!Current.IsName("in"))
        {
            throw Error(Current, "expected 'in' in a for statement but found " + Current);
        }

        _index++;
        JinjaExpression sequence = ParseExpression(false);

        JinjaExpression condition = null;
        if (Current.IsName("if"))
        {
            _index++;
            condition = ParseExpression(false);
        }

        if (Current.IsName("recursive"))
        {
            throw Error(Current, "recursive loops are not supported");
        }

        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        IList<JinjaNode> body = ParseBody(new[] { "endfor", "else" }, out string terminator);
        IList<JinjaNode> elseBody = null;
        if (terminator == "else")
        {
            ConsumeSimpleTerminator();
            elseBody = ParseBody(new[] { "endfor" }, out terminator);
        }

        ConsumeSimpleTerminator();
        return new JinjaForNode(targets, sequence, condition, body, elseBody);
    }

    private IList<string> ParseAssignmentTarget()
    {
        var path = new List<string> { ExpectName() };
        while (Current.IsOperator("."))
        {
            _index++;
            path.Add(ExpectName());
        }

        return path;
    }

    private JinjaNode ParseSet()
    {
        _index++;
        var targets = new List<IList<string>> { ParseAssignmentTarget() };
        while (Current.IsOperator(","))
        {
            _index++;
            targets.Add(ParseAssignmentTarget());
        }

        if (Current.IsOperator("="))
        {
            _index++;
            JinjaExpression value = ParseExpression(true);
            ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
            return new JinjaSetNode(targets, value, null, null);
        }

        JinjaExpression filter = null;
        if (Current.IsOperator("|"))
        {
            filter = ParseFilterChain(null);
        }

        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        IList<JinjaNode> body = ParseBody(new[] { "endset" }, out string terminator);
        ConsumeSimpleTerminator();
        return new JinjaSetNode(targets, null, body, filter);
    }

    private JinjaNode ParseMacro()
    {
        _index++;
        string name = ExpectName();
        IList<JinjaArgument> parameters = ParseParameterList();
        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        _macroNames.Add(new HashSet<string>(StringComparer.Ordinal));
        IList<JinjaNode> body;
        HashSet<string> referenced;
        try
        {
            body = ParseBody(new[] { "endmacro" }, out string terminator);
        }
        finally
        {
            referenced = _macroNames[_macroNames.Count - 1];
            _macroNames.RemoveAt(_macroNames.Count - 1);
        }

        ConsumeSimpleTerminator();
        return new JinjaMacroNode(name, parameters, body, Accepts(parameters, referenced, "varargs"),
            Accepts(parameters, referenced, "kwargs"));
    }

    private static bool Accepts(IList<JinjaArgument> parameters, HashSet<string> referenced, string name)
    {
        foreach (JinjaArgument parameter in parameters)
        {
            if (parameter.Name == name)
            {
                return false;
            }
        }

        return referenced.Contains(name);
    }

    private JinjaNode ParseCallBlock()
    {
        _index++;
        IList<JinjaArgument> parameters = Current.IsOperator("(")
            ? ParseParameterList()
            : new List<JinjaArgument>();
        JinjaExpression call = ParseExpression(true);
        if (!(call is JinjaCallExpression callExpression))
        {
            throw Error(Current, "a call block must call a macro");
        }

        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        IList<JinjaNode> body = ParseBody(new[] { "endcall" }, out string terminator);
        ConsumeSimpleTerminator();
        return new JinjaCallNode(parameters, callExpression, body);
    }

    private JinjaNode ParseFilterBlock()
    {
        _index++;
        string name = ExpectName();
        IList<JinjaArgument> arguments = Current.IsOperator("(")
            ? ParseCallArguments()
            : new List<JinjaArgument>();
        JinjaExpression filter = ParseFilterChain(new JinjaFilterExpression(null, name, arguments));
        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        IList<JinjaNode> body = ParseBody(new[] { "endfilter" }, out string terminator);
        ConsumeSimpleTerminator();
        return new JinjaFilterBlockNode(filter, body);
    }

    private JinjaNode ParseSimpleBlock(string endKeyword, bool newScope)
    {
        _index++;
        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        IList<JinjaNode> body = ParseBody(new[] { endKeyword }, out string terminator);
        ConsumeSimpleTerminator();
        return new JinjaBlockNode(body, newScope);
    }

    private JinjaNode ParseWith()
    {
        _index++;
        var assignments = new List<JinjaNode>();
        while (Current.Kind == JinjaTokenKind.Name)
        {
            var target = new List<IList<string>> { ParseAssignmentTarget() };
            ExpectOperator("=");
            JinjaExpression value = ParseExpression(true);
            assignments.Add(new JinjaSetNode(target, value, null, null));
            if (Current.IsOperator(","))
            {
                _index++;
            }
        }

        ExpectKind(JinjaTokenKind.BlockEnd, "'%}'");
        IList<JinjaNode> body = ParseBody(new[] { "endwith" }, out string terminator);
        ConsumeSimpleTerminator();
        assignments.AddRange(body);
        return new JinjaBlockNode(assignments, true);
    }

    private IList<JinjaArgument> ParseParameterList()
    {
        var parameters = new List<JinjaArgument>();
        ExpectOperator("(");
        while (!Current.IsOperator(")"))
        {
            string name = ExpectName();
            JinjaExpression defaultValue = null;
            if (Current.IsOperator("="))
            {
                _index++;
                defaultValue = ParseExpression(true);
            }

            parameters.Add(new JinjaArgument(name, defaultValue));
            if (Current.IsOperator(","))
            {
                _index++;
                continue;
            }

            break;
        }

        ExpectOperator(")");
        return parameters;
    }

    private IList<JinjaArgument> ParseCallArguments()
    {
        var arguments = new List<JinjaArgument>();
        ExpectOperator("(");
        while (!Current.IsOperator(")"))
        {
            if (Current.Kind == JinjaTokenKind.Name && Peek(1).IsOperator("=") && !Peek(2).IsOperator("="))
            {
                string name = Advance().Value;
                _index++;
                arguments.Add(new JinjaArgument(name, ParseExpression(true)));
            }
            else
            {
                arguments.Add(new JinjaArgument(null, ParseExpression(true)));
            }

            if (Current.IsOperator(","))
            {
                _index++;
                continue;
            }

            break;
        }

        ExpectOperator(")");
        return arguments;
    }

    private JinjaExpression ParseExpression(bool withConditional)
    {
        EnterDepth();
        try
        {
            JinjaExpression expression = ParseOr();
            if (!withConditional || !Current.IsName("if"))
            {
                return expression;
            }

            _index++;
            JinjaExpression condition = ParseOr();
            JinjaExpression otherwise = null;
            if (Current.IsName("else"))
            {
                _index++;
                otherwise = ParseExpression(true);
            }

            return Track(new JinjaConditionalExpression(condition, expression, otherwise),
                condition, expression, otherwise);
        }
        finally
        {
            _depth--;
        }
    }

    private void EnterDepth()
    {
        TemplateBudget.EnsureStack();
        _depth++;
        if (_depth > _maximumDepth)
        {
            throw DepthError();
        }
    }

    private ChatTemplateException DepthError()
        => Error(Current, "the template nests more than " + _maximumDepth + " levels deep ("
            + nameof(TemplateLimits) + "." + nameof(TemplateLimits.MaxNestingDepth) + ")");

    /// <summary>
    /// Records how tall the tree under a new node is, and refuses it when that height on top of the
    /// nesting the parser is already inside would exceed the limit.
    /// </summary>
    /// <remarks>
    /// A chain such as <c>a + b + c</c>, <c>x | f | g</c> or <c>a.b.c</c> is parsed by a loop, not by
    /// recursion, but it builds a tree one level deeper for every link, and the renderer walks that tree
    /// recursively. Counting the parser's own recursion is therefore not enough: the height of what it
    /// builds is checked as well.
    /// </remarks>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The new node.</param>
    /// <param name="children">Its child expressions; null entries are ignored.</param>
    /// <returns>The node.</returns>
    private T Track<T>(T node, params JinjaExpression[] children)
        where T : JinjaExpression
    {
        int tallest = 0;
        foreach (JinjaExpression child in children)
        {
            if (child != null && child.Height > tallest)
            {
                tallest = child.Height;
            }
        }

        node.Height = tallest + 1;
        if (node.Height + _depth > _maximumDepth)
        {
            throw DepthError();
        }

        return node;
    }

    private T TrackArguments<T>(T node, JinjaExpression source, IList<JinjaArgument> arguments)
        where T : JinjaExpression
    {
        var children = new List<JinjaExpression> { source };
        foreach (JinjaArgument argument in arguments)
        {
            children.Add(argument.Value);
        }

        return Track(node, children.ToArray());
    }

    private JinjaExpression ParseOr()
    {
        JinjaExpression left = ParseAnd();
        while (Current.IsName("or"))
        {
            _index++;
            JinjaExpression right = ParseAnd();
            left = Track(new JinjaBinaryExpression(JinjaOperator.Or, left, right), left, right);
        }

        return left;
    }

    private JinjaExpression ParseAnd()
    {
        JinjaExpression left = ParseNot();
        while (Current.IsName("and"))
        {
            _index++;
            JinjaExpression right = ParseNot();
            left = Track(new JinjaBinaryExpression(JinjaOperator.And, left, right), left, right);
        }

        return left;
    }

    private JinjaExpression ParseNot()
    {
        if (Current.IsName("not") && !Peek(1).IsName("in"))
        {
            _index++;
            EnterDepth();
            try
            {
                JinjaExpression operand = ParseNot();
                return Track(new JinjaUnaryExpression(JinjaOperator.Not, operand), operand);
            }
            finally
            {
                _depth--;
            }
        }

        return ParseComparison();
    }

    private JinjaExpression ParseComparison()
    {
        JinjaExpression left = ParseAdditive();

        while (true)
        {
            JinjaToken token = Current;
            JinjaOperator operation;
            if (token.IsOperator("=="))
            {
                operation = JinjaOperator.Equal;
            }
            else if (token.IsOperator("!="))
            {
                operation = JinjaOperator.NotEqual;
            }
            else if (token.IsOperator("<"))
            {
                operation = JinjaOperator.Less;
            }
            else if (token.IsOperator("<="))
            {
                operation = JinjaOperator.LessOrEqual;
            }
            else if (token.IsOperator(">"))
            {
                operation = JinjaOperator.Greater;
            }
            else if (token.IsOperator(">="))
            {
                operation = JinjaOperator.GreaterOrEqual;
            }
            else if (token.IsName("in"))
            {
                operation = JinjaOperator.In;
            }
            else if (token.IsName("not") && Peek(1).IsName("in"))
            {
                _index++;
                operation = JinjaOperator.NotIn;
            }
            else
            {
                return left;
            }

            _index++;
            JinjaExpression right = ParseAdditive();
            left = Track(new JinjaBinaryExpression(operation, left, right), left, right);
        }
    }

    private JinjaExpression ParseAdditive()
    {
        JinjaExpression left = ParseConcat();
        while (Current.IsOperator("+") || Current.IsOperator("-"))
        {
            JinjaOperator operation = Advance().Value == "+" ? JinjaOperator.Add : JinjaOperator.Subtract;
            JinjaExpression right = ParseConcat();
            left = Track(new JinjaBinaryExpression(operation, left, right), left, right);
        }

        return left;
    }

    private JinjaExpression ParseConcat()
    {
        JinjaExpression left = ParseMultiplicative();
        while (Current.IsOperator("~"))
        {
            _index++;
            JinjaExpression right = ParseMultiplicative();
            left = Track(new JinjaBinaryExpression(JinjaOperator.Concat, left, right), left, right);
        }

        return left;
    }

    private JinjaExpression ParseMultiplicative()
    {
        JinjaExpression left = ParsePower();
        while (Current.IsOperator("*") || Current.IsOperator("/")
            || Current.IsOperator("//") || Current.IsOperator("%"))
        {
            string spelling = Advance().Value;
            JinjaOperator operation;
            switch (spelling)
            {
                case "*":
                    operation = JinjaOperator.Multiply;
                    break;
                case "/":
                    operation = JinjaOperator.Divide;
                    break;
                case "//":
                    operation = JinjaOperator.FloorDivide;
                    break;
                default:
                    operation = JinjaOperator.Modulo;
                    break;
            }

            JinjaExpression right = ParsePower();
            left = Track(new JinjaBinaryExpression(operation, left, right), left, right);
        }

        return left;
    }

    private JinjaExpression ParsePower()
    {
        JinjaExpression left = ParseUnary(true);
        while (Current.IsOperator("**"))
        {
            _index++;
            JinjaExpression right = ParseUnary(true);
            left = Track(new JinjaBinaryExpression(JinjaOperator.Power, left, right), left, right);
        }

        return left;
    }

    private JinjaExpression ParseUnary(bool withFilter)
    {
        JinjaExpression node;
        if (Current.IsOperator("-") || Current.IsOperator("+"))
        {
            JinjaOperator operation = Advance().Value == "-" ? JinjaOperator.Negate : JinjaOperator.Positive;
            EnterDepth();
            try
            {
                JinjaExpression operand = ParseUnary(false);
                node = Track(new JinjaUnaryExpression(operation, operand), operand);
            }
            finally
            {
                _depth--;
            }
        }
        else
        {
            node = ParsePostfix(ParsePrimary());
        }

        return withFilter ? ParseFilterExpression(node) : node;
    }

    private JinjaExpression ParsePostfix(JinjaExpression node)
    {
        while (true)
        {
            if (Current.IsOperator("."))
            {
                _index++;
                if (Current.Kind == JinjaTokenKind.Integer)
                {
                    var index = new JinjaLiteralExpression(Advance().Number);
                    node = Track(new JinjaSubscriptExpression(node, index), node, index);
                    continue;
                }

                node = Track(new JinjaAttributeExpression(node, ExpectName()), node);
                continue;
            }

            if (Current.IsOperator("["))
            {
                node = ParseSubscript(node);
                continue;
            }

            if (Current.IsOperator("("))
            {
                IList<JinjaArgument> arguments = ParseCallArguments();
                node = TrackArguments(new JinjaCallExpression(node, arguments), node, arguments);
                continue;
            }

            return node;
        }
    }

    private JinjaExpression ParseFilterExpression(JinjaExpression node)
    {
        while (true)
        {
            if (Current.IsOperator("|"))
            {
                node = ParseFilterChain(node);
                continue;
            }

            if (Current.IsName("is"))
            {
                node = ParseTest(node);
                continue;
            }

            if (Current.IsOperator("("))
            {
                IList<JinjaArgument> arguments = ParseCallArguments();
                node = TrackArguments(new JinjaCallExpression(node, arguments), node, arguments);
                continue;
            }

            return node;
        }
    }

    private JinjaExpression ParseFilterChain(JinjaExpression node)
    {
        while (Current.IsOperator("|"))
        {
            _index++;
            string name = ExpectName();
            IList<JinjaArgument> arguments = Current.IsOperator("(")
                ? ParseCallArguments()
                : new List<JinjaArgument>();
            node = TrackArguments(new JinjaFilterExpression(node, name, arguments), node, arguments);
        }

        return node;
    }

    private JinjaExpression ParseTest(JinjaExpression node)
    {
        _index++;
        bool negated = false;
        if (Current.IsName("not"))
        {
            negated = true;
            _index++;
        }

        string name = ExpectName();
        IList<JinjaArgument> arguments = new List<JinjaArgument>();
        if (Current.IsOperator("("))
        {
            arguments = ParseCallArguments();
        }
        else if (IsTestArgumentStart(Current))
        {
            arguments.Add(new JinjaArgument(null, ParsePostfix(ParsePrimary())));
        }

        return TrackArguments(new JinjaTestExpression(node, name, arguments, negated), node, arguments);
    }

    private static bool IsTestArgumentStart(JinjaToken token)
    {
        switch (token.Kind)
        {
            case JinjaTokenKind.String:
            case JinjaTokenKind.Integer:
            case JinjaTokenKind.Float:
                return true;
            case JinjaTokenKind.Name:
                return Array.IndexOf(TestArgumentStoppers, token.Value) < 0;
            case JinjaTokenKind.Operator:
                return token.Value == "[" || token.Value == "{";
            default:
                return false;
        }
    }

    private JinjaExpression ParseSubscript(JinjaExpression node)
    {
        ExpectOperator("[");
        JinjaExpression start = null;
        if (!Current.IsOperator(":"))
        {
            start = ParseExpression(true);
        }

        if (!Current.IsOperator(":"))
        {
            ExpectOperator("]");
            return Track(new JinjaSubscriptExpression(node, start), node, start);
        }

        _index++;
        JinjaExpression stop = null;
        if (!Current.IsOperator(":") && !Current.IsOperator("]"))
        {
            stop = ParseExpression(true);
        }

        JinjaExpression step = null;
        if (Current.IsOperator(":"))
        {
            _index++;
            if (!Current.IsOperator("]"))
            {
                step = ParseExpression(true);
            }
        }

        ExpectOperator("]");
        return Track(new JinjaSliceExpression(node, start, stop, step), node, start, stop, step);
    }

    private JinjaExpression ParsePrimary()
    {
        JinjaToken token = Current;
        switch (token.Kind)
        {
            case JinjaTokenKind.Name:
                _index++;
                switch (token.Value)
                {
                    case "true":
                    case "True":
                        return new JinjaLiteralExpression(true);
                    case "false":
                    case "False":
                        return new JinjaLiteralExpression(false);
                    case "none":
                    case "None":
                        return new JinjaLiteralExpression(null);
                    default:
                        if (_macroNames.Count > 0)
                        {
                            _macroNames[_macroNames.Count - 1].Add(token.Value);
                        }

                        return new JinjaNameExpression(token.Value);
                }

            case JinjaTokenKind.String:
                {
                    _index++;
                    if (Current.Kind != JinjaTokenKind.String)
                    {
                        return new JinjaLiteralExpression(token.Value);
                    }

                    // Adjacent literals are one string, as in Python. A builder keeps a long run linear.
                    var text = new StringBuilder(token.Value);
                    while (Current.Kind == JinjaTokenKind.String)
                    {
                        text.Append(Advance().Value);
                    }

                    return new JinjaLiteralExpression(text.ToString());
                }

            case JinjaTokenKind.Integer:
            case JinjaTokenKind.Float:
                _index++;
                return new JinjaLiteralExpression(token.Number);

            case JinjaTokenKind.Operator:
                if (token.Value == "(")
                {
                    return ParseParenthesized();
                }

                if (token.Value == "[")
                {
                    return ParseListLiteral();
                }

                if (token.Value == "{")
                {
                    return ParseMappingLiteral();
                }

                break;
        }

        throw Error(token, "unexpected " + token + " in an expression");
    }

    private JinjaExpression ParseParenthesized()
    {
        ExpectOperator("(");
        if (Current.IsOperator(")"))
        {
            _index++;
            return new JinjaSequenceExpression(new List<JinjaExpression>());
        }

        var items = new List<JinjaExpression> { ParseExpression(true) };
        bool isTuple = false;
        while (Current.IsOperator(","))
        {
            isTuple = true;
            _index++;
            if (Current.IsOperator(")"))
            {
                break;
            }

            items.Add(ParseExpression(true));
        }

        ExpectOperator(")");
        return isTuple ? Track(new JinjaSequenceExpression(items), items.ToArray()) : items[0];
    }

    private JinjaExpression ParseListLiteral()
    {
        ExpectOperator("[");
        var items = new List<JinjaExpression>();
        while (!Current.IsOperator("]"))
        {
            items.Add(ParseExpression(true));
            if (Current.IsOperator(","))
            {
                _index++;
                continue;
            }

            break;
        }

        ExpectOperator("]");
        return Track(new JinjaSequenceExpression(items), items.ToArray());
    }

    private JinjaExpression ParseMappingLiteral()
    {
        ExpectOperator("{");
        var keys = new List<JinjaExpression>();
        var values = new List<JinjaExpression>();
        while (!Current.IsOperator("}"))
        {
            keys.Add(ParseExpression(true));
            ExpectOperator(":");
            values.Add(ParseExpression(true));
            if (Current.IsOperator(","))
            {
                _index++;
                continue;
            }

            break;
        }

        ExpectOperator("}");
        var children = new List<JinjaExpression>(keys);
        children.AddRange(values);
        return Track(new JinjaMappingExpression(keys, values), children.ToArray());
    }
}
