using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The global functions a chat template can call: <c>range</c>, <c>namespace</c>, <c>dict</c>,
/// <c>raise_exception</c> and <c>strftime_now</c>.
/// </summary>
internal static class JinjaFunctions
{
    /// <summary>The names that resolve to a global function.</summary>
    private static readonly string[] Names =
    {
        "range", "namespace", "dict", "raise_exception", "strftime_now",
    };

    /// <summary>Reports whether a name is a global function.</summary>
    /// <param name="name">The name to test.</param>
    /// <returns>True when a global function has that name.</returns>
    internal static bool Exists(string name)
    {
        foreach (string candidate in Names)
        {
            if (candidate == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Invokes a global function.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The positional arguments.</param>
    /// <param name="keywords">The keyword arguments.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ChatTemplateException">
    /// The function is unknown, was misused, or is <c>raise_exception</c>.
    /// </exception>
    internal static object Invoke(
        string name, IList<object> arguments, IDictionary<string, object> keywords)
    {
        switch (name)
        {
            case "range":
                return Range(arguments);
            case "namespace":
                {
                    var ns = new JinjaNamespace();
                    if (keywords != null)
                    {
                        foreach (KeyValuePair<string, object> pair in keywords)
                        {
                            ns.Set(pair.Key, pair.Value);
                        }
                    }

                    if (arguments != null && arguments.Count > 0
                        && arguments[0] is IDictionary<string, object> seed)
                    {
                        foreach (KeyValuePair<string, object> pair in seed)
                        {
                            ns.Set(pair.Key, pair.Value);
                        }
                    }

                    return ns;
                }

            case "dict":
                {
                    IDictionary<string, object> mapping = JinjaValues.NewMapping();
                    if (arguments != null && arguments.Count > 0
                        && arguments[0] is IDictionary<string, object> seed)
                    {
                        foreach (KeyValuePair<string, object> pair in seed)
                        {
                            mapping[pair.Key] = pair.Value;
                        }
                    }

                    if (keywords != null)
                    {
                        foreach (KeyValuePair<string, object> pair in keywords)
                        {
                            mapping[pair.Key] = pair.Value;
                        }
                    }

                    return mapping;
                }

            case "raise_exception":
                throw new ChatTemplateException(arguments == null || arguments.Count == 0
                    ? "The chat template called raise_exception()."
                    : JinjaValues.ToDisplayString(arguments[0]));
            case "strftime_now":
                return JinjaStrftime.Format(
                    arguments == null || arguments.Count == 0
                        ? "%Y-%m-%d"
                        : JinjaValues.ToDisplayString(arguments[0]),
                    DateTime.Now);
            default:
                throw JinjaValues.RuntimeError("no function named '" + name + "'.");
        }
    }

    /// <summary>The largest list <c>range()</c> will build before it refuses.</summary>
    private const long MaximumRangeLength = 10000000L;

    private static object Range(IList<object> arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            throw JinjaValues.RuntimeError("'range' needs at least one argument.");
        }

        long start = 0;
        long stop;
        long step = 1;
        if (arguments.Count == 1)
        {
            stop = JinjaValues.ToIndex(arguments[0], "range stop");
        }
        else
        {
            start = JinjaValues.ToIndex(arguments[0], "range start");
            stop = JinjaValues.ToIndex(arguments[1], "range stop");
            if (arguments.Count > 2)
            {
                step = JinjaValues.ToIndex(arguments[2], "range step");
            }
        }

        if (step == 0)
        {
            throw JinjaValues.RuntimeError("a range step cannot be zero.");
        }

        double estimate = step > 0
            ? Math.Ceiling(((double)stop - start) / step)
            : Math.Ceiling(((double)start - stop) / -(double)step);
        if (estimate > MaximumRangeLength)
        {
            throw JinjaValues.RuntimeError(
                "'range' would produce more than "
                + MaximumRangeLength.ToString(CultureInfo.InvariantCulture) + " values.");
        }

        var result = new List<object>();
        if (step > 0)
        {
            for (long value = start; value < stop; value += step)
            {
                result.Add(value);
            }
        }
        else
        {
            for (long value = start; value > stop; value += step)
            {
                result.Add(value);
            }
        }

        return result;
    }
}
