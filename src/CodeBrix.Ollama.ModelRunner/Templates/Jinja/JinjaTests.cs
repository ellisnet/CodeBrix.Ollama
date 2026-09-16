using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The test library behind <c>value is name</c>. The names and their meanings follow Jinja, which in
/// turn follows Python - so a boolean passes <c>number</c>, a string passes <c>iterable</c> and
/// <c>sequence</c>, and only a mapping passes <c>mapping</c>.
/// </summary>
internal static class JinjaTests
{
    /// <summary>Runs a test.</summary>
    /// <param name="name">The test name.</param>
    /// <param name="value">The tested value.</param>
    /// <param name="arguments">The test arguments.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ChatTemplateException">The test is unknown.</exception>
    internal static bool Apply(string name, object value, IList<object> arguments)
    {
        switch (name)
        {
            case "defined":
                return !JinjaValues.IsUndefined(value);
            case "undefined":
                return JinjaValues.IsUndefined(value);
            case "none":
            case "null":
                return value == null;
            case "string":
                return value is string;
            case "number":
                return JinjaValues.TryGetNumber(value, out double _, out bool _);
            case "integer":
                return value is long || value is bool;
            case "float":
                return value is double;
            case "mapping":
                return value is IDictionary<string, object>;
            case "iterable":
                return value is string || value is IList<object> || value is IDictionary<string, object>;
            case "sequence":
                return value is string || value is IList<object> || value is IDictionary<string, object>;
            case "boolean":
                return value is bool;
            case "true":
                return value is bool flagTrue && flagTrue;
            case "false":
                return value is bool flagFalse && !flagFalse;
            case "lower":
                return value is string lowerText && lowerText == lowerText.ToLowerInvariant();
            case "upper":
                return value is string upperText && upperText == upperText.ToUpperInvariant();
            case "even":
                return IsInteger(value, out long even) && even % 2 == 0;
            case "odd":
                return IsInteger(value, out long odd) && odd % 2 != 0;
            case "divisibleby":
                {
                    long divisor = JinjaValues.ToIndex(First(arguments), "divisor");
                    return divisor != 0 && IsInteger(value, out long dividend) && dividend % divisor == 0;
                }

            case "in":
                return JinjaValues.Contains(First(arguments), value);
            case "eq":
            case "equalto":
            case "==":
                return JinjaValues.AreEqual(value, First(arguments));
            case "ne":
            case "!=":
                return !JinjaValues.AreEqual(value, First(arguments));
            case "lt":
            case "lessthan":
            case "<":
                return JinjaValues.Compare(value, First(arguments)) < 0;
            case "le":
            case "<=":
                return JinjaValues.Compare(value, First(arguments)) <= 0;
            case "gt":
            case "greaterthan":
            case ">":
                return JinjaValues.Compare(value, First(arguments)) > 0;
            case "ge":
            case ">=":
                return JinjaValues.Compare(value, First(arguments)) >= 0;
            case "sameas":
                return ReferenceEquals(value, First(arguments))
                    || (value is bool left && First(arguments) is bool right && left == right)
                    || (value == null && First(arguments) == null);
            case "callable":
                return value is JinjaMacro || value is JinjaFunction || value is JinjaBoundMethod;
            case "escaped":
                return false;
            case "filter":
                return JinjaFilters.Exists(JinjaValues.ToDisplayString(value));
            case "test":
                return Exists(JinjaValues.ToDisplayString(value));
            default:
                throw JinjaValues.RuntimeError("no test named '" + name + "'.");
        }
    }

    /// <summary>Reports whether a test name is known.</summary>
    /// <param name="name">The test name.</param>
    /// <returns>True when the test exists.</returns>
    internal static bool Exists(string name)
    {
        switch (name)
        {
            case "defined":
            case "undefined":
            case "none":
            case "null":
            case "string":
            case "number":
            case "integer":
            case "float":
            case "mapping":
            case "iterable":
            case "sequence":
            case "boolean":
            case "true":
            case "false":
            case "lower":
            case "upper":
            case "even":
            case "odd":
            case "divisibleby":
            case "in":
            case "eq":
            case "equalto":
            case "==":
            case "ne":
            case "!=":
            case "lt":
            case "lessthan":
            case "<":
            case "le":
            case "<=":
            case "gt":
            case "greaterthan":
            case ">":
            case "ge":
            case ">=":
            case "sameas":
            case "callable":
            case "escaped":
            case "filter":
            case "test":
                return true;
            default:
                return false;
        }
    }

    private static object First(IList<object> arguments)
    {
        return arguments != null && arguments.Count > 0 ? arguments[0] : null;
    }

    private static bool IsInteger(object value, out long number)
    {
        if (JinjaValues.TryGetNumber(value, out double real, out bool isInteger) && isInteger)
        {
            number = (long)real;
            return true;
        }

        number = 0;
        return false;
    }
}
