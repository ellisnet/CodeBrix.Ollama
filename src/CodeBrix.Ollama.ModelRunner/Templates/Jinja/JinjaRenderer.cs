using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Walks a parsed template and produces its text. It holds the scope chain, so one renderer serves one
/// render; a <see cref="JinjaTemplate"/> can be rendered any number of times because a fresh renderer
/// is made each time.
/// </summary>
internal sealed class JinjaRenderer
{
    /// <summary>How deeply macros may call one another before the render is abandoned.</summary>
    private const int MaximumMacroDepth = 200;

    private readonly JinjaScope _root;

    private int _loopDepth;

    private int _macroDepth;

    /// <summary>Initializes a new instance of the <see cref="JinjaRenderer"/> class.</summary>
    /// <param name="variables">The variables the template sees; the values are normalized on the way in.</param>
    internal JinjaRenderer(IReadOnlyDictionary<string, object> variables)
    {
        _root = new JinjaScope(null);
        if (variables == null)
        {
            return;
        }

        foreach (KeyValuePair<string, object> pair in variables)
        {
            _root.Set(pair.Key, JinjaValues.Normalize(pair.Value));
        }
    }

    /// <summary>Renders a template body.</summary>
    /// <param name="body">The top-level nodes.</param>
    /// <returns>The rendered text.</returns>
    internal string Render(IList<JinjaNode> body)
    {
        var output = new StringBuilder();
        Execute(body, _root, output);
        return output.ToString();
    }

    private void Execute(IList<JinjaNode> body, JinjaScope scope, StringBuilder output)
    {
        foreach (JinjaNode node in body)
        {
            ExecuteNode(node, scope, output);
        }
    }

    private void ExecuteNode(JinjaNode node, JinjaScope scope, StringBuilder output)
    {
        switch (node)
        {
            case JinjaTextNode text:
                output.Append(text.Text);
                return;
            case JinjaOutputNode print:
                output.Append(JinjaValues.ToDisplayString(Evaluate(print.Expression, scope)));
                return;
            case JinjaIfNode conditional:
                ExecuteIf(conditional, scope, output);
                return;
            case JinjaForNode loop:
                ExecuteFor(loop, scope, output);
                return;
            case JinjaSetNode assignment:
                ExecuteSet(assignment, scope);
                return;
            case JinjaMacroNode macro:
                scope.Set(macro.Name, new JinjaMacro(macro, scope));
                return;
            case JinjaCallNode call:
                ExecuteCall(call, scope, output);
                return;
            case JinjaFilterBlockNode filter:
                {
                    var captured = new StringBuilder();
                    Execute(filter.Body, scope, captured);
                    output.Append(JinjaValues.ToDisplayString(
                        ApplyFilterChain(filter.Filter, captured.ToString(), scope)));
                    return;
                }

            case JinjaBlockNode block:
                Execute(block.Body, block.NewScope ? new JinjaScope(scope) : scope, output);
                return;
            case JinjaLoopControlNode control:
                throw new JinjaLoopSignal(control.IsBreak);
            case JinjaExpressionStatementNode statement:
                Evaluate(statement.Expression, scope);
                return;
            default:
                throw JinjaValues.RuntimeError("unsupported template node.");
        }
    }

    private void ExecuteIf(JinjaIfNode node, JinjaScope scope, StringBuilder output)
    {
        foreach (JinjaIfBranch branch in node.Branches)
        {
            if (JinjaValues.IsTruthy(Evaluate(branch.Condition, scope)))
            {
                Execute(branch.Body, scope, output);
                return;
            }
        }

        if (node.ElseBody != null)
        {
            Execute(node.ElseBody, scope, output);
        }
    }

    private void ExecuteFor(JinjaForNode node, JinjaScope scope, StringBuilder output)
    {
        IList<object> source = JinjaValues.Iterate(Evaluate(node.Sequence, scope));
        IList<object> items;
        if (node.Condition == null)
        {
            items = source;
        }
        else
        {
            items = new List<object>();
            foreach (object candidate in source)
            {
                var probe = new JinjaScope(scope);
                BindTargets(probe, node.Targets, candidate);
                if (JinjaValues.IsTruthy(Evaluate(node.Condition, probe)))
                {
                    items.Add(candidate);
                }
            }
        }

        if (items.Count == 0)
        {
            if (node.ElseBody != null)
            {
                Execute(node.ElseBody, scope, output);
            }

            return;
        }

        _loopDepth++;
        var loop = new JinjaLoop(items, _loopDepth);
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                loop.Index0 = i;
                var iteration = new JinjaScope(scope);
                BindTargets(iteration, node.Targets, items[i]);
                iteration.Set("loop", loop);
                try
                {
                    Execute(node.Body, iteration, output);
                }
                catch (JinjaLoopSignal signal)
                {
                    if (signal.IsBreak)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            _loopDepth--;
        }
    }

    private static void BindTargets(JinjaScope scope, IList<string> targets, object item)
    {
        if (targets.Count == 1)
        {
            scope.Set(targets[0], item);
            return;
        }

        IList<object> parts = JinjaValues.Iterate(item);
        if (parts.Count != targets.Count)
        {
            throw JinjaValues.RuntimeError(
                "cannot unpack " + parts.Count + " values into " + targets.Count + " loop variables.");
        }

        for (int i = 0; i < targets.Count; i++)
        {
            scope.Set(targets[i], parts[i]);
        }
    }

    private void ExecuteSet(JinjaSetNode node, JinjaScope scope)
    {
        object value;
        if (node.Value != null)
        {
            value = Evaluate(node.Value, scope);
        }
        else
        {
            var captured = new StringBuilder();
            Execute(node.Body, scope, captured);
            value = ApplyFilterChain(node.Filter, captured.ToString(), scope);
        }

        if (node.Targets.Count == 1)
        {
            AssignTarget(node.Targets[0], value, scope);
            return;
        }

        IList<object> parts = JinjaValues.Iterate(value);
        if (parts.Count != node.Targets.Count)
        {
            throw JinjaValues.RuntimeError(
                "cannot unpack " + parts.Count + " values into " + node.Targets.Count + " names.");
        }

        for (int i = 0; i < node.Targets.Count; i++)
        {
            AssignTarget(node.Targets[i], parts[i], scope);
        }
    }

    private void AssignTarget(IList<string> path, object value, JinjaScope scope)
    {
        if (path.Count == 1)
        {
            scope.Set(path[0], value);
            return;
        }

        object target = scope.TryGet(path[0], out object found) ? found : JinjaUndefined.Named(path[0]);
        for (int i = 1; i < path.Count - 1; i++)
        {
            target = JinjaValues.GetAttribute(target, path[i]);
        }

        string last = path[path.Count - 1];
        switch (target)
        {
            case JinjaNamespace ns:
                ns.Set(last, value);
                return;
            case IDictionary<string, object> mapping:
                mapping[last] = value;
                return;
            default:
                throw JinjaValues.RuntimeError(
                    "cannot assign '" + last + "' on " + JinjaValues.DescribeType(target)
                    + "; only a namespace() or a mapping supports attribute assignment.");
        }
    }

    private void ExecuteCall(JinjaCallNode node, JinjaScope scope, StringBuilder output)
    {
        object callee = Evaluate(node.Call.Callee, scope);
        EvaluateArguments(node.Call.Arguments, scope, out IList<object> arguments,
            out IDictionary<string, object> keywords);
        var caller = new JinjaMacro(new JinjaMacroNode("caller", node.Parameters, node.Body), scope);
        if (!(callee is JinjaMacro macro))
        {
            throw JinjaValues.RuntimeError("a call block must call a macro.");
        }

        output.Append(InvokeMacro(macro, arguments, keywords, caller));
    }

    private object ApplyFilterChain(JinjaExpression filter, object value, JinjaScope scope)
    {
        if (filter == null)
        {
            return value;
        }

        var node = (JinjaFilterExpression)filter;
        object source = node.Source == null ? value : ApplyFilterChain(node.Source, value, scope);
        EvaluateArguments(node.Arguments, scope, out IList<object> arguments,
            out IDictionary<string, object> keywords);
        return JinjaFilters.Apply(node.Name, source, arguments, keywords);
    }

    private void EvaluateArguments(
        IList<JinjaArgument> definitions,
        JinjaScope scope,
        out IList<object> arguments,
        out IDictionary<string, object> keywords)
    {
        arguments = new List<object>();
        keywords = JinjaValues.NewMapping();
        foreach (JinjaArgument argument in definitions)
        {
            object value = Evaluate(argument.Value, scope);
            if (argument.Name == null)
            {
                arguments.Add(value);
            }
            else
            {
                keywords[argument.Name] = value;
            }
        }
    }

    /// <summary>Calls a macro and returns the text its body rendered.</summary>
    /// <param name="macro">The macro.</param>
    /// <param name="arguments">The positional arguments.</param>
    /// <param name="keywords">The keyword arguments.</param>
    /// <param name="caller">The <c>caller</c> macro from a <c>{% call %}</c> block, or null.</param>
    /// <returns>The rendered text.</returns>
    internal string InvokeMacro(
        JinjaMacro macro, IList<object> arguments, IDictionary<string, object> keywords, JinjaMacro caller)
    {
        var scope = new JinjaScope(macro.Closure);
        IList<JinjaArgument> parameters = macro.Definition.Parameters;
        var consumed = new HashSet<string>();

        for (int i = 0; i < parameters.Count; i++)
        {
            JinjaArgument parameter = parameters[i];
            object value;
            if (arguments != null && i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (keywords != null && keywords.TryGetValue(parameter.Name, out object named))
            {
                value = named;
                consumed.Add(parameter.Name);
            }
            else if (parameter.Value != null)
            {
                value = Evaluate(parameter.Value, scope);
            }
            else
            {
                value = JinjaUndefined.Named(parameter.Name);
            }

            scope.Set(parameter.Name, value);
        }

        if (macro.Definition.AcceptsVarargs)
        {
            var extra = new List<object>();
            for (int i = parameters.Count; arguments != null && i < arguments.Count; i++)
            {
                extra.Add(arguments[i]);
            }

            scope.Set("varargs", extra);
        }

        IDictionary<string, object> leftover = JinjaValues.NewMapping();
        if (keywords != null)
        {
            foreach (KeyValuePair<string, object> pair in keywords)
            {
                if (!consumed.Contains(pair.Key))
                {
                    leftover[pair.Key] = pair.Value;
                }
            }
        }

        if (macro.Definition.AcceptsKeywords)
        {
            scope.Set("kwargs", leftover);
        }
        else if (leftover.Count > 0)
        {
            string unexpected = string.Empty;
            foreach (KeyValuePair<string, object> pair in leftover)
            {
                unexpected = pair.Key;
                break;
            }

            throw JinjaValues.RuntimeError(
                "macro '" + macro.Definition.Name + "' takes no keyword argument '" + unexpected + "'.");
        }

        if (caller != null)
        {
            scope.Set("caller", caller);
        }

        var output = new StringBuilder();
        _macroDepth++;
        try
        {
            if (_macroDepth > MaximumMacroDepth)
            {
                throw JinjaValues.RuntimeError(
                    "macro '" + macro.Definition.Name + "' nested more than " + MaximumMacroDepth
                    + " calls deep; the template looks to be recursing without an end.");
            }

            Execute(macro.Definition.Body, scope, output);
        }
        catch (JinjaLoopSignal signal)
        {
            throw JinjaValues.RuntimeError(
                "'" + (signal.IsBreak ? "break" : "continue") + "' inside macro '" + macro.Definition.Name
                + "' has no loop to act on; a loop outside the macro is not one of its own.");
        }
        finally
        {
            _macroDepth--;
        }

        return output.ToString();
    }

    /// <summary>Evaluates an expression.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="scope">The scope to resolve names in.</param>
    /// <returns>The value.</returns>
    internal object Evaluate(JinjaExpression expression, JinjaScope scope)
    {
        switch (expression)
        {
            case JinjaLiteralExpression literal:
                return literal.Value;
            case JinjaNameExpression name:
                if (scope.TryGet(name.Name, out object bound))
                {
                    return bound;
                }

                return JinjaFunctions.Exists(name.Name)
                    ? new JinjaFunction(name.Name)
                    : (object)JinjaUndefined.Named(name.Name);
            case JinjaSequenceExpression sequence:
                {
                    var items = new List<object>(sequence.Items.Count);
                    foreach (JinjaExpression item in sequence.Items)
                    {
                        items.Add(Evaluate(item, scope));
                    }

                    return items;
                }

            case JinjaMappingExpression mapping:
                {
                    IDictionary<string, object> result = JinjaValues.NewMapping();
                    for (int i = 0; i < mapping.Keys.Count; i++)
                    {
                        result[JinjaValues.ToDisplayString(Evaluate(mapping.Keys[i], scope))] =
                            Evaluate(mapping.Values[i], scope);
                    }

                    return result;
                }

            case JinjaUnaryExpression unary:
                return EvaluateUnary(unary, scope);
            case JinjaBinaryExpression binary:
                return EvaluateBinary(binary, scope);
            case JinjaConditionalExpression conditional:
                if (JinjaValues.IsTruthy(Evaluate(conditional.Condition, scope)))
                {
                    return Evaluate(conditional.WhenTrue, scope);
                }

                return conditional.WhenFalse == null
                    ? JinjaUndefined.Instance
                    : Evaluate(conditional.WhenFalse, scope);
            case JinjaAttributeExpression attribute:
                return JinjaValues.GetAttribute(Evaluate(attribute.Target, scope), attribute.Name);
            case JinjaSubscriptExpression subscript:
                return JinjaValues.GetItem(
                    Evaluate(subscript.Target, scope), Evaluate(subscript.Index, scope));
            case JinjaSliceExpression slice:
                return JinjaValues.Slice(
                    Evaluate(slice.Target, scope),
                    slice.Start == null ? null : Evaluate(slice.Start, scope),
                    slice.Stop == null ? null : Evaluate(slice.Stop, scope),
                    slice.Step == null ? null : Evaluate(slice.Step, scope));
            case JinjaCallExpression call:
                return EvaluateCall(call, scope);
            case JinjaFilterExpression filter:
                {
                    object source = Evaluate(filter.Source, scope);
                    EvaluateArguments(filter.Arguments, scope, out IList<object> arguments,
                        out IDictionary<string, object> keywords);
                    return JinjaFilters.Apply(filter.Name, source, arguments, keywords);
                }

            case JinjaTestExpression test:
                {
                    object source = Evaluate(test.Source, scope);
                    EvaluateArguments(test.Arguments, scope, out IList<object> arguments,
                        out IDictionary<string, object> _);
                    bool result = JinjaTests.Apply(test.Name, source, arguments);
                    return test.Negated ? !result : result;
                }

            default:
                throw JinjaValues.RuntimeError("unsupported expression.");
        }
    }

    private object EvaluateUnary(JinjaUnaryExpression node, JinjaScope scope)
    {
        object operand = Evaluate(node.Operand, scope);
        switch (node.Operation)
        {
            case JinjaOperator.Not:
                return !JinjaValues.IsTruthy(operand);
            case JinjaOperator.Negate:
                return JinjaValues.Arithmetic(JinjaOperator.Subtract, 0L, operand);
            default:
                return operand;
        }
    }

    private object EvaluateBinary(JinjaBinaryExpression node, JinjaScope scope)
    {
        if (node.Operation == JinjaOperator.And)
        {
            object left = Evaluate(node.Left, scope);
            return JinjaValues.IsTruthy(left) ? Evaluate(node.Right, scope) : left;
        }

        if (node.Operation == JinjaOperator.Or)
        {
            object left = Evaluate(node.Left, scope);
            return JinjaValues.IsTruthy(left) ? left : Evaluate(node.Right, scope);
        }

        object a = Evaluate(node.Left, scope);
        object b = Evaluate(node.Right, scope);
        switch (node.Operation)
        {
            case JinjaOperator.Equal:
                return JinjaValues.AreEqual(a, b);
            case JinjaOperator.NotEqual:
                return !JinjaValues.AreEqual(a, b);
            case JinjaOperator.Less:
                return JinjaValues.Compare(a, b) < 0;
            case JinjaOperator.LessOrEqual:
                return JinjaValues.Compare(a, b) <= 0;
            case JinjaOperator.Greater:
                return JinjaValues.Compare(a, b) > 0;
            case JinjaOperator.GreaterOrEqual:
                return JinjaValues.Compare(a, b) >= 0;
            case JinjaOperator.In:
                return JinjaValues.Contains(b, a);
            case JinjaOperator.NotIn:
                return !JinjaValues.Contains(b, a);
            default:
                return JinjaValues.Arithmetic(node.Operation, a, b);
        }
    }

    private object EvaluateCall(JinjaCallExpression node, JinjaScope scope)
    {
        object callee = Evaluate(node.Callee, scope);
        EvaluateArguments(node.Arguments, scope, out IList<object> arguments,
            out IDictionary<string, object> keywords);
        switch (callee)
        {
            case JinjaFunction function:
                return JinjaFunctions.Invoke(function.Name, arguments, keywords);
            case JinjaBoundMethod method:
                return JinjaMethods.Invoke(method, arguments, keywords);
            case JinjaMacro macro:
                return InvokeMacro(macro, arguments, keywords, null);
            case JinjaUndefined undefined:
                throw JinjaValues.RuntimeError(undefined.Describe() + " and cannot be called.");
            default:
                throw JinjaValues.RuntimeError(
                    JinjaValues.DescribeType(callee) + " is not callable.");
        }
    }
}
