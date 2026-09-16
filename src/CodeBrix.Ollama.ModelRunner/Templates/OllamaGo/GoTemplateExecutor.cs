// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go (BSD-3-Clause);

/// <summary>
/// Renders a parsed Go text/template. One executor renders one template once; the values it walks are
/// the untyped <see cref="GoMap"/>, <see cref="GoSlice"/> and <see cref="GoStruct"/> shapes, which
/// stand in for what Go reaches through reflection.
/// </summary>
internal sealed class GoTemplateExecutor
{
    private const int SignalNone = 0;
    private const int SignalBreak = 1;
    private const int SignalContinue = 2;
    private const int MaxExecDepth = 1000;

    private static readonly object Missing = new object();

    private readonly GoTemplate _template;
    private readonly StringBuilder _output;
    private readonly List<KeyValuePair<string, object>> _vars = new List<KeyValuePair<string, object>>();
    private GoTemplateTree _tree;
    private int _depth;

    /// <summary>Creates an executor for a template.</summary>
    /// <param name="template">The template to render.</param>
    internal GoTemplateExecutor(GoTemplate template)
    {
        _template = template;
        _output = new StringBuilder();
    }

    /// <summary>
    /// Renders the template.
    /// </summary>
    /// <param name="data">The value dot starts out as.</param>
    /// <returns>The rendered text.</returns>
    /// <exception cref="ChatTemplateException">The template could not be rendered.</exception>
    internal string Execute(object data)
    {
        _tree = _template.Tree;
        if (_tree?.Root == null)
        {
            throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                "template: {0} is an incomplete or empty template", GoQuote.Quote(_template.Name)));
        }

        _vars.Add(new KeyValuePair<string, object>("$", data));
        Walk(data, _tree.Root);
        return _output.ToString();
    }

    private static ChatTemplateException Error(string format, params object[] args)
        => new ChatTemplateException(args.Length == 0
            ? format
            : string.Format(CultureInfo.InvariantCulture, format, args));

    private int Mark() => _vars.Count;

    private void Pop(int mark)
    {
        if (_vars.Count > mark)
        {
            _vars.RemoveRange(mark, _vars.Count - mark);
        }
    }

    private void Push(string name, object value) => _vars.Add(new KeyValuePair<string, object>(name, value));

    private void SetVar(string name, object value)
    {
        for (int i = _vars.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_vars[i].Key, name, StringComparison.Ordinal))
            {
                _vars[i] = new KeyValuePair<string, object>(name, value);
                return;
            }
        }

        throw Error("undefined variable: {0}", name);
    }

    private void SetTopVar(int n, object value)
    {
        int index = _vars.Count - n;
        _vars[index] = new KeyValuePair<string, object>(_vars[index].Key, value);
    }

    private object VarValue(string name)
    {
        for (int i = _vars.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_vars[i].Key, name, StringComparison.Ordinal))
            {
                return _vars[i].Value;
            }
        }

        throw Error("undefined variable: {0}", name);
    }

    private int Walk(object dot, GoNode node)
    {
        switch (node)
        {
            case GoActionNode action:
            {
                object value = EvalPipeline(dot, action.Pipe);
                if (action.Pipe.Decl.Count == 0)
                {
                    PrintValue(action, value);
                }

                return SignalNone;
            }

            case GoJumpNode jump:
                return jump.NodeType == GoNodeType.Break ? SignalBreak : SignalContinue;
            case GoCommentNode _:
                return SignalNone;
            case GoListNode list:
            {
                foreach (GoNode child in list.Nodes)
                {
                    int signal = Walk(dot, child);
                    if (signal != SignalNone)
                    {
                        return signal;
                    }
                }

                return SignalNone;
            }

            case GoTextNode text:
                _output.Append(text.Text);
                return SignalNone;
            case GoBranchNode branch when branch.NodeType == GoNodeType.Range:
                return WalkRange(dot, branch);
            case GoBranchNode branch:
                return WalkIfOrWith(dot, branch);
            case GoTemplateCallNode call:
                return WalkTemplateCall(dot, call);
            default:
                throw Error("unknown node: {0}", node);
        }
    }

    private int WalkIfOrWith(object dot, GoBranchNode branch)
    {
        int mark = Mark();
        try
        {
            object value = EvalPipeline(dot, branch.Pipe);
            if (GoValue.IsTrue(value))
            {
                return branch.NodeType == GoNodeType.With ? Walk(value, branch.List) : Walk(dot, branch.List);
            }

            return branch.ElseList != null ? Walk(dot, branch.ElseList) : SignalNone;
        }
        finally
        {
            Pop(mark);
        }
    }

    private int WalkRange(object dot, GoBranchNode range)
    {
        int outerMark = Mark();
        try
        {
            object value = EvalPipeline(dot, range.Pipe);
            int mark = Mark();
            bool iterated = false;
            int signal = SignalNone;
            switch (GoValue.KindOf(value))
            {
                case GoKind.Slice:
                {
                    GoSlice slice = (GoSlice)value;
                    if (slice.Items.Count > 0)
                    {
                        iterated = true;
                        for (int i = 0; i < slice.Items.Count && signal != SignalBreak; i++)
                        {
                            signal = OneIteration(range, mark, (long)i, slice.Items[i]);
                        }
                    }

                    break;
                }

                case GoKind.Map:
                {
                    GoMap map = (GoMap)value;
                    if (map.Entries.Count > 0)
                    {
                        iterated = true;
                        List<string> keys = map.SortedKeys();
                        for (int i = 0; i < keys.Count && signal != SignalBreak; i++)
                        {
                            signal = OneIteration(range, mark, keys[i], map.Entries[keys[i]]);
                        }
                    }

                    break;
                }

                //a string is deliberately absent: Go's text/template does not range over one, and
                //reports "range can't iterate over ..." from the default arm below
                case GoKind.Int:
                {
                    long count = GoValue.ToInt64(value);
                    if (range.Pipe.Decl.Count > 1)
                    {
                        throw Error("can't use {0} to iterate over more than one variable", GoFormat.Print(value));
                    }

                    if (count > 0)
                    {
                        iterated = true;
                        for (long i = 0; i < count && signal != SignalBreak; i++)
                        {
                            signal = OneIteration(range, mark, null, i);
                        }
                    }

                    break;
                }

                case GoKind.Uint:
                {
                    ulong count = GoValue.ToUInt64(value);
                    if (range.Pipe.Decl.Count > 1)
                    {
                        throw Error("can't use {0} to iterate over more than one variable", GoFormat.Print(value));
                    }

                    if (count > 0)
                    {
                        iterated = true;
                        for (ulong i = 0; i < count && signal != SignalBreak; i++)
                        {
                            signal = OneIteration(range, mark, null, i);
                        }
                    }

                    break;
                }

                case GoKind.Invalid:
                case GoKind.Nil:
                    break;
                default:
                    throw Error("range can't iterate over {0}", GoFormat.Print(value));
            }

            if (!iterated && range.ElseList != null)
            {
                return Walk(dot, range.ElseList);
            }

            return SignalNone;
        }
        finally
        {
            Pop(outerMark);
        }
    }

    private int OneIteration(GoBranchNode range, int mark, object index, object element)
    {
        if (range.Pipe.Decl.Count > 0)
        {
            if (range.Pipe.IsAssign)
            {
                SetVar(range.Pipe.Decl[0].Ident[0], range.Pipe.Decl.Count > 1 ? index : element);
            }
            else
            {
                SetTopVar(1, element);
            }
        }

        if (range.Pipe.Decl.Count > 1)
        {
            if (range.Pipe.IsAssign)
            {
                SetVar(range.Pipe.Decl[1].Ident[0], element);
            }
            else
            {
                SetTopVar(2, index);
            }
        }

        try
        {
            int signal = Walk(element, range.List);
            return signal == SignalContinue ? SignalNone : signal;
        }
        finally
        {
            Pop(mark);
        }
    }

    private int WalkTemplateCall(object dot, GoTemplateCallNode call)
    {
        GoTemplateTree tree = _template.Lookup(call.Name);
        if (tree == null)
        {
            throw Error("template {0} not defined", GoQuote.Quote(call.Name));
        }

        if (_depth == MaxExecDepth)
        {
            throw Error("exceeded maximum template depth ({0})", MaxExecDepth);
        }

        object value = EvalPipeline(dot, call.Pipe);
        GoTemplateTree outerTree = _tree;
        List<KeyValuePair<string, object>> outerVars = new List<KeyValuePair<string, object>>(_vars);
        _depth++;
        _tree = tree;
        _vars.Clear();
        _vars.Add(new KeyValuePair<string, object>("$", value));
        try
        {
            return Walk(value, tree.Root);
        }
        finally
        {
            _depth--;
            _tree = outerTree;
            _vars.Clear();
            _vars.AddRange(outerVars);
        }
    }

    private object EvalPipeline(object dot, GoPipeNode pipe)
    {
        if (pipe == null)
        {
            return Missing;
        }

        object value = Missing;
        foreach (GoCommandNode command in pipe.Cmds)
        {
            value = EvalCommand(dot, command, value);
        }

        foreach (GoVariableNode variable in pipe.Decl)
        {
            if (pipe.IsAssign)
            {
                SetVar(variable.Ident[0], value);
            }
            else
            {
                Push(variable.Ident[0], value);
            }
        }

        return value;
    }

    private object EvalCommand(object dot, GoCommandNode cmd, object final)
    {
        GoNode firstWord = cmd.Args[0];
        switch (firstWord)
        {
            case GoFieldNode field:
                return EvalFieldChain(dot, dot, field.Ident, 0, cmd.Args, final);
            case GoChainNode chain:
            {
                if (chain.Field.Count == 0)
                {
                    throw Error("internal error: no fields in chain");
                }

                if (chain.Node.NodeType == GoNodeType.Nil)
                {
                    throw Error("indirection through explicit nil in {0}", chain);
                }

                object receiver = EvalArg(dot, chain.Node);
                return EvalFieldChain(dot, receiver, chain.Field.ToArray(), 0, cmd.Args, final);
            }

            case GoIdentifierNode identifier:
                return EvalFunction(dot, identifier, cmd.Args, final);
            case GoPipeNode pipe:
                NotAFunction(cmd.Args, final);
                return EvalPipeline(dot, pipe);
            case GoVariableNode variable:
            {
                object value = VarValue(variable.Ident[0]);
                if (variable.Ident.Length == 1)
                {
                    NotAFunction(cmd.Args, final);
                    return value;
                }

                string[] rest = new string[variable.Ident.Length - 1];
                Array.Copy(variable.Ident, 1, rest, 0, rest.Length);
                return EvalFieldChain(dot, value, rest, 0, cmd.Args, final);
            }
        }

        NotAFunction(cmd.Args, final);
        switch (firstWord)
        {
            case GoBoolNode b:
                return b.True;
            case GoDotNode _:
                return dot;
            case GoNilNode _:
                throw Error("nil is not a command");
            case GoNumberNode number:
                return IdealConstant(number);
            case GoStringNode s:
                return s.Text;
        }

        throw Error("can't evaluate command {0}", GoQuote.Quote(firstWord.ToString()));
    }

    private void NotAFunction(List<GoNode> args, object final)
    {
        if (args.Count > 1 || !ReferenceEquals(final, Missing))
        {
            throw Error("can't give argument to non-function {0}", args[0]);
        }
    }

    private object IdealConstant(GoNumberNode constant)
    {
        if (constant.IsFloat && !IsHexInt(constant.Text) && !IsRuneInt(constant.Text)
            && constant.Text.IndexOfAny(new[] { '.', 'e', 'E', 'p', 'P' }) >= 0)
        {
            return constant.Float64;
        }

        if (constant.IsInt)
        {
            return constant.Int64;
        }

        if (constant.IsUint)
        {
            throw Error("{0} overflows int", constant.Text);
        }

        return GoUndefined.Instance;
    }

    private static bool IsRuneInt(string s) => s.Length > 0 && s[0] == '\'';

    private static bool IsHexInt(string s)
        => s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X')
           && s.IndexOf('p') < 0 && s.IndexOf('P') < 0;

    private object EvalFieldChain(object dot, object receiver, string[] ident, int start, List<GoNode> args,
        object final)
    {
        for (int i = start; i < ident.Length - 1; i++)
        {
            receiver = EvalField(ident[i], null, Missing, receiver);
        }

        return EvalField(ident[ident.Length - 1], args, final, receiver);
    }

    private object EvalField(string fieldName, List<GoNode> args, object final, object receiver)
    {
        if (receiver is GoUndefined || ReferenceEquals(receiver, Missing))
        {
            return GoUndefined.Instance;
        }

        bool hasArgs = (args != null && args.Count > 1) || !ReferenceEquals(final, Missing);
        switch (receiver)
        {
            case GoStruct structure:
                if (structure.TryGetField(fieldName, out GoStructField field))
                {
                    if (hasArgs)
                    {
                        throw Error("{0} has arguments but cannot be invoked as function", fieldName);
                    }

                    return field.Value;
                }

                break;
            case GoMap map:
                if (hasArgs)
                {
                    throw Error("{0} is not a method but has arguments", fieldName);
                }

                return map.Entries.TryGetValue(fieldName, out object value) ? value : map.MissingValue;
            case null:
                throw Error("nil pointer evaluating interface {{}}.{0}", fieldName);
        }

        throw Error("can't evaluate field {0} in type {1}", fieldName, GoValue.TypeName(receiver));
    }

    private object EvalFunction(object dot, GoIdentifierNode node, List<GoNode> args, object final)
    {
        if (!_template.Funcs.TryGetValue(node.Ident, out GoFunction function))
        {
            throw Error("{0} is not a defined function", GoQuote.Quote(node.Ident));
        }

        return EvalCall(dot, function, args, final);
    }

    private object EvalCall(object dot, GoFunction function, List<GoNode> args, object final)
    {
        List<GoNode> argNodes = new List<GoNode>();
        if (args != null)
        {
            for (int i = 1; i < args.Count; i++)
            {
                argNodes.Add(args[i]);
            }
        }

        bool hasFinal = !ReferenceEquals(final, Missing);
        int argCount = argNodes.Count + (hasFinal ? 1 : 0);
        if (argCount < function.MinArgs)
        {
            throw Error("wrong number of args for {0}: want at least {1} got {2}", function.Name,
                function.MinArgs, argCount);
        }

        if (function.MaxArgs >= 0 && argCount > function.MaxArgs)
        {
            throw Error("wrong number of args for {0}: want {1} got {2}", function.Name, function.MaxArgs,
                argCount);
        }

        if (function.Name == "and" || function.Name == "or")
        {
            bool wanted = function.Name == "or";
            object last = null;
            foreach (GoNode argNode in argNodes)
            {
                last = EvalArg(dot, argNode);
                if (GoValue.IsTrue(last) == wanted)
                {
                    return last;
                }
            }

            return hasFinal ? final : last;
        }

        List<object> values = new List<object>(argCount);
        foreach (GoNode argNode in argNodes)
        {
            values.Add(EvalArg(dot, argNode));
        }

        if (hasFinal)
        {
            values.Add(final);
        }

        return function.Invoke(values);
    }

    private object EvalArg(object dot, GoNode node)
    {
        switch (node)
        {
            case GoDotNode _:
                return dot;
            case GoNilNode _:
                return null;
            case GoFieldNode field:
                return EvalFieldChain(dot, dot, field.Ident, 0, null, Missing);
            case GoVariableNode variable:
            {
                object value = VarValue(variable.Ident[0]);
                if (variable.Ident.Length == 1)
                {
                    return value;
                }

                string[] rest = new string[variable.Ident.Length - 1];
                Array.Copy(variable.Ident, 1, rest, 0, rest.Length);
                return EvalFieldChain(dot, value, rest, 0, null, Missing);
            }

            case GoPipeNode pipe:
                return EvalPipeline(dot, pipe);
            case GoIdentifierNode identifier:
                return EvalFunction(dot, identifier, null, Missing);
            case GoChainNode chain:
            {
                object receiver = EvalArg(dot, chain.Node);
                return EvalFieldChain(dot, receiver, chain.Field.ToArray(), 0, null, Missing);
            }

            case GoBoolNode b:
                return b.True;
            case GoNumberNode number:
                return IdealConstant(number);
            case GoStringNode s:
                return s.Text;
        }

        throw Error("can't handle {0} as an argument", node);
    }

    private void PrintValue(GoNode node, object value)
    {
        if (ReferenceEquals(value, Missing))
        {
            _output.Append("<no value>");
            return;
        }

        _output.Append(GoFormat.Print(value));
    }
}
