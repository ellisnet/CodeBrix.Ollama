using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The filter library. The set covers what real chat templates use, and each filter keeps Jinja's own
/// argument names so that a template written for Jinja calls it the same way here.
/// </summary>
internal static class JinjaFilters
{
    /// <summary>Reports whether a filter name is known.</summary>
    /// <param name="name">The filter name.</param>
    /// <returns>True when the filter exists.</returns>
    internal static bool Exists(string name)
    {
        switch (name)
        {
            case "tojson":
            case "to_json":
            case "pprint":
            case "trim":
            case "lower":
            case "upper":
            case "title":
            case "capitalize":
            case "length":
            case "count":
            case "join":
            case "replace":
            case "default":
            case "d":
            case "first":
            case "last":
            case "list":
            case "string":
            case "int":
            case "float":
            case "safe":
            case "e":
            case "escape":
            case "forceescape":
            case "indent":
            case "striptags":
            case "wordcount":
            case "truncate":
            case "reverse":
            case "sort":
            case "unique":
            case "map":
            case "select":
            case "reject":
            case "selectattr":
            case "rejectattr":
            case "items":
            case "dictsort":
            case "sum":
            case "min":
            case "max":
            case "abs":
            case "round":
            case "batch":
            case "slice":
            case "center":
            case "format":
            case "urlencode":
            case "attr":
            case "groupby":
            case "lstrip":
            case "rstrip":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Applies a filter.</summary>
    /// <param name="name">The filter name.</param>
    /// <param name="value">The filtered value.</param>
    /// <param name="arguments">The positional arguments.</param>
    /// <param name="keywords">The keyword arguments.</param>
    /// <returns>The filtered value.</returns>
    /// <exception cref="ChatTemplateException">The filter is unknown or was misused.</exception>
    internal static object Apply(
        string name, object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        switch (name)
        {
            // 'pprint' is deliberately an alias for 'tojson' here. Jinja's own pprint filter calls
            // Python's pprint.pformat, which prints a Python repr; chat templates only ever reach for
            // it to eyeball a structure, and a JSON rendering is both closer to what the surrounding
            // template text wants and something this library already produces. The difference is
            // quoting and the True/False/None spellings, so anything that round-trips through tojson
            // is unaffected.
            case "tojson":
            case "to_json":
            case "pprint":
                {
                    object indent = Argument(arguments, keywords, 0, "indent");
                    return JinjaJson.Serialize(
                        value,
                        indent == null || indent is JinjaUndefined ? -1 : JinjaValues.ToIndex(indent, "indent"));
                }

            case "trim":
                return JinjaMethods.Trim(
                    AsText(value), Argument(arguments, keywords, 0, "chars"), true, true);
            case "lstrip":
                return JinjaMethods.Trim(
                    AsText(value), Argument(arguments, keywords, 0, "chars"), true, false);
            case "rstrip":
                return JinjaMethods.Trim(
                    AsText(value), Argument(arguments, keywords, 0, "chars"), false, true);
            case "lower":
                return AsText(value).ToLowerInvariant();
            case "upper":
                return AsText(value).ToUpperInvariant();
            case "title":
                return JinjaValues.ToDisplayString(
                    JinjaMethods.Invoke(new JinjaBoundMethod(AsText(value), "title"), null, null));
            case "capitalize":
                return JinjaValues.ToDisplayString(
                    JinjaMethods.Invoke(new JinjaBoundMethod(AsText(value), "capitalize"), null, null));
            case "length":
            case "count":
                return (long)Length(value);
            case "join":
                return Join(value, arguments, keywords);
            case "replace":
                return JinjaMethods.Invoke(
                    new JinjaBoundMethod(AsText(value), "replace"), Flatten(arguments, keywords, "old", "new", "count"), null);
            case "default":
            case "d":
                {
                    object fallback = HasArgument(arguments, keywords, 0, "default_value")
                        ? Argument(arguments, keywords, 0, "default_value")
                        : string.Empty;
                    object boolean = Argument(arguments, keywords, 1, "boolean");
                    bool useTruthiness = boolean != null && JinjaValues.IsTruthy(boolean);
                    if (JinjaValues.IsUndefined(value))
                    {
                        return fallback;
                    }

                    return useTruthiness && !JinjaValues.IsTruthy(value) ? fallback : value;
                }

            case "first":
                {
                    IList<object> items = JinjaValues.Iterate(value);
                    return items.Count > 0 ? items[0] : JinjaUndefined.Instance;
                }

            case "last":
                {
                    IList<object> items = JinjaValues.Iterate(value);
                    return items.Count > 0 ? items[items.Count - 1] : JinjaUndefined.Instance;
                }

            case "list":
                return new List<object>(JinjaValues.Iterate(value));
            case "string":
                return JinjaValues.ToDisplayString(value);
            case "int":
                return ToInteger(value, Argument(arguments, keywords, 0, "default"));
            case "float":
                return ToFloat(value, Argument(arguments, keywords, 0, "default"));
            case "safe":
                return value;
            case "e":
            case "escape":
            case "forceescape":
                return Escape(AsText(value));
            case "indent":
                return Indent(value, arguments, keywords);
            case "striptags":
                return StripTags(AsText(value));
            case "wordcount":
                return (long)WordCount(AsText(value));
            case "truncate":
                return Truncate(AsText(value), arguments, keywords);
            case "reverse":
                {
                    if (value is string text)
                    {
                        char[] characters = text.ToCharArray();
                        Array.Reverse(characters);
                        return new string(characters);
                    }

                    var reversed = new List<object>(JinjaValues.Iterate(value));
                    reversed.Reverse();
                    return reversed;
                }

            case "sort":
                return Sort(value, arguments, keywords);
            case "unique":
                return Unique(value, arguments, keywords);
            case "map":
                return Map(value, arguments, keywords);
            case "select":
                return SelectByTest(value, arguments, keywords, true, false);
            case "reject":
                return SelectByTest(value, arguments, keywords, false, false);
            case "selectattr":
                return SelectByTest(value, arguments, keywords, true, true);
            case "rejectattr":
                return SelectByTest(value, arguments, keywords, false, true);
            case "items":
                return value is IDictionary<string, object> mapping
                    ? JinjaMethods.Invoke(new JinjaBoundMethod(mapping, "items"), null, null)
                    : new List<object>();
            case "dictsort":
                return DictSort(value, arguments, keywords);
            case "sum":
                return Sum(value, arguments, keywords);
            case "min":
                return Extremum(value, arguments, keywords, true);
            case "max":
                return Extremum(value, arguments, keywords, false);
            case "abs":
                {
                    if (!JinjaValues.TryGetNumber(value, out double number, out bool isInteger))
                    {
                        throw JinjaValues.RuntimeError("'abs' needs a number.");
                    }

                    return isInteger ? (object)(long)Math.Abs(number) : Math.Abs(number);
                }

            case "round":
                return Round(value, arguments, keywords);
            case "batch":
                return Batch(value, arguments, keywords);
            case "slice":
                return SliceInto(value, arguments, keywords);
            case "center":
                {
                    object width = Argument(arguments, keywords, 0, "width");
                    return Center(
                        AsText(value),
                        width == null || width is JinjaUndefined ? 80 : JinjaValues.ToIndex(width, "width"),
                        ' ');
                }

            case "format":
                return PercentFormat(AsText(value), arguments == null || arguments.Count == 1
                    ? (arguments == null || arguments.Count == 0 ? null : arguments[0])
                    : new List<object>(arguments));
            case "urlencode":
                return UrlEncode(value);
            case "attr":
                return JinjaValues.GetAttribute(
                    value, JinjaValues.ToDisplayString(Argument(arguments, keywords, 0, "name")));
            case "groupby":
                return GroupBy(value, arguments, keywords);
            default:
                throw JinjaValues.RuntimeError("no filter named '" + name + "'.");
        }
    }

    private static string AsText(object value) => JinjaValues.ToDisplayString(value);

    private static bool HasArgument(
        IList<object> arguments, IDictionary<string, object> keywords, int position, string name)
    {
        return (arguments != null && position < arguments.Count)
            || (keywords != null && name != null && keywords.ContainsKey(name));
    }

    private static object Argument(
        IList<object> arguments, IDictionary<string, object> keywords, int position, string name)
    {
        if (arguments != null && position < arguments.Count)
        {
            return arguments[position];
        }

        if (keywords != null && name != null && keywords.TryGetValue(name, out object value))
        {
            return value;
        }

        return null;
    }

    private static IList<object> Flatten(
        IList<object> arguments, IDictionary<string, object> keywords, params string[] names)
    {
        var collected = new List<object>();
        for (int i = 0; i < names.Length; i++)
        {
            collected.Add(Argument(arguments, keywords, i, names[i]));
        }

        return collected;
    }

    private static int Length(object value)
    {
        switch (value)
        {
            case string text:
                return text.Length;
            case IDictionary<string, object> mapping:
                return mapping.Count;
            case IList<object> list:
                return list.Count;
            case JinjaUndefined undefined:
                throw JinjaValues.RuntimeError(undefined.Describe() + " and has no length.");
            case null:
                throw JinjaValues.RuntimeError("None has no length.");
            default:
                throw JinjaValues.RuntimeError(JinjaValues.DescribeType(value) + " has no length.");
        }
    }

    private static object Join(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        string separator = AsText(Argument(arguments, keywords, 0, "d") ?? string.Empty);
        object attribute = Argument(arguments, keywords, 1, "attribute");
        var builder = new StringBuilder();
        bool first = true;
        foreach (object item in JinjaValues.Iterate(value))
        {
            if (!first)
            {
                builder.Append(separator);
            }

            first = false;
            builder.Append(AsText(attribute == null ? item : ReadAttributePath(item, attribute)));
        }

        return builder.ToString();
    }

    /// <summary>Reads a possibly dotted attribute path, as Jinja's <c>attribute=</c> arguments allow.</summary>
    /// <param name="value">The value to read from.</param>
    /// <param name="path">The attribute name, dotted path or integer index.</param>
    /// <returns>The value found, or undefined.</returns>
    internal static object ReadAttributePath(object value, object path)
    {
        if (JinjaValues.TryGetNumber(path, out double index, out bool isInteger) && isInteger && !(path is bool))
        {
            return JinjaValues.GetItem(value, (long)index);
        }

        object current = value;
        foreach (string part in AsText(path).Split('.'))
        {
            current = long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long position)
                ? JinjaValues.GetItem(current, position)
                : JinjaValues.GetAttribute(current, part);
        }

        return current;
    }

    private static object ToInteger(object value, object fallback)
    {
        object result = fallback ?? 0L;
        if (value is string text)
        {
            if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }

            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
                ? (long)real
                : result;
        }

        return JinjaValues.TryGetNumber(value, out double number, out bool _) ? (long)number : result;
    }

    private static object ToFloat(object value, object fallback)
    {
        object result = fallback ?? 0d;
        if (value is string text)
        {
            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : result;
        }

        return JinjaValues.TryGetNumber(value, out double number, out bool _) ? number : result;
    }

    private static string Escape(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&#34;")
            .Replace("'", "&#39;");
    }

    private static object Indent(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        string text = AsText(value);
        object width = Argument(arguments, keywords, 0, "width");
        bool first = JinjaValues.IsTruthy(Argument(arguments, keywords, 1, "first"));
        bool blank = JinjaValues.IsTruthy(Argument(arguments, keywords, 2, "blank"));
        string padding = width is string padText
            ? padText
            : new string(' ', width == null || width is JinjaUndefined ? 4 : JinjaValues.ToIndex(width, "width"));

        string[] lines = text.Split('\n');
        var builder = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            bool empty = lines[i].Trim().Length == 0;
            bool indentThisLine = (i > 0 || first) && (!empty || blank);
            if (indentThisLine)
            {
                builder.Append(padding);
            }

            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    private static string StripTags(string text)
    {
        var builder = new StringBuilder();
        bool inTag = false;
        foreach (char character in text)
        {
            if (character == '<')
            {
                inTag = true;
                continue;
            }

            if (character == '>')
            {
                inTag = false;
                continue;
            }

            if (!inTag)
            {
                builder.Append(character);
            }
        }

        string[] words = builder.ToString().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }

    private static int WordCount(string text)
    {
        return text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static object Truncate(string text, IList<object> arguments, IDictionary<string, object> keywords)
    {
        object lengthValue = Argument(arguments, keywords, 0, "length");
        int length = lengthValue == null || lengthValue is JinjaUndefined
            ? 255
            : JinjaValues.ToIndex(lengthValue, "length");
        bool killWords = JinjaValues.IsTruthy(Argument(arguments, keywords, 1, "killwords"));
        object endValue = Argument(arguments, keywords, 2, "end");
        string end = endValue == null || endValue is JinjaUndefined ? "..." : AsText(endValue);
        object leewayValue = Argument(arguments, keywords, 3, "leeway");
        int leeway = leewayValue == null || leewayValue is JinjaUndefined
            ? 5
            : JinjaValues.ToIndex(leewayValue, "leeway");

        if (text.Length <= length + leeway)
        {
            return text;
        }

        int cut = Math.Max(0, length - end.Length);
        if (killWords)
        {
            return text.Substring(0, cut) + end;
        }

        string head = text.Substring(0, cut);
        int space = head.LastIndexOf(' ');
        return (space < 0 ? head : head.Substring(0, space)) + end;
    }

    private static object Sort(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        bool reverse = JinjaValues.IsTruthy(Argument(arguments, keywords, 0, "reverse"));
        bool caseSensitive = JinjaValues.IsTruthy(Argument(arguments, keywords, 1, "case_sensitive"));
        object attribute = Argument(arguments, keywords, 2, "attribute");
        var items = new List<object>(JinjaValues.Iterate(value));
        StableSort(items, (left, right) =>
        {
            object a = attribute == null ? left : ReadAttributePath(left, attribute);
            object b = attribute == null ? right : ReadAttributePath(right, attribute);
            if (!caseSensitive && a is string leftText && b is string rightText)
            {
                return string.CompareOrdinal(leftText.ToLowerInvariant(), rightText.ToLowerInvariant());
            }

            return JinjaValues.Compare(a, b);
        });

        if (reverse)
        {
            items.Reverse();
        }

        return items;
    }

    private static object Unique(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        bool caseSensitive = JinjaValues.IsTruthy(Argument(arguments, keywords, 0, "case_sensitive"));
        object attribute = Argument(arguments, keywords, 1, "attribute");
        var seen = new List<object>();
        var result = new List<object>();
        foreach (object item in JinjaValues.Iterate(value))
        {
            object key = attribute == null ? item : ReadAttributePath(item, attribute);
            if (!caseSensitive && key is string text)
            {
                key = text.ToLowerInvariant();
            }

            bool found = false;
            foreach (object previous in seen)
            {
                if (JinjaValues.AreEqual(previous, key))
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                continue;
            }

            seen.Add(key);
            result.Add(item);
        }

        return result;
    }

    private static object Map(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        object attribute = keywords != null && keywords.TryGetValue("attribute", out object named) ? named : null;
        var result = new List<object>();
        if (attribute != null)
        {
            object fallback = keywords != null && keywords.TryGetValue("default", out object given) ? given : null;
            foreach (object item in JinjaValues.Iterate(value))
            {
                object mapped = ReadAttributePath(item, attribute);
                result.Add(JinjaValues.IsUndefined(mapped) && fallback != null ? fallback : mapped);
            }

            return result;
        }

        if (arguments == null || arguments.Count == 0)
        {
            throw JinjaValues.RuntimeError("'map' needs a filter name or an attribute.");
        }

        string filterName = AsText(arguments[0]);
        var extra = new List<object>();
        for (int i = 1; i < arguments.Count; i++)
        {
            extra.Add(arguments[i]);
        }

        foreach (object item in JinjaValues.Iterate(value))
        {
            result.Add(Apply(filterName, item, extra, keywords));
        }

        return result;
    }

    private static object SelectByTest(
        object value, IList<object> arguments, IDictionary<string, object> keywords, bool keep, bool byAttribute)
    {
        var positional = new List<object>(arguments ?? new List<object>());
        object attribute = null;
        if (byAttribute)
        {
            if (positional.Count == 0)
            {
                throw JinjaValues.RuntimeError("'selectattr' needs an attribute name.");
            }

            attribute = positional[0];
            positional.RemoveAt(0);
        }

        string testName = null;
        if (positional.Count > 0)
        {
            testName = AsText(positional[0]);
            positional.RemoveAt(0);
        }

        var result = new List<object>();
        foreach (object item in JinjaValues.Iterate(value))
        {
            object candidate = byAttribute ? ReadAttributePath(item, attribute) : item;
            bool matched = testName == null
                ? JinjaValues.IsTruthy(candidate)
                : JinjaTests.Apply(testName, candidate, positional);
            if (matched == keep)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static object DictSort(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        if (!(value is IDictionary<string, object> mapping))
        {
            throw JinjaValues.RuntimeError("'dictsort' needs a mapping.");
        }

        bool caseSensitive = JinjaValues.IsTruthy(Argument(arguments, keywords, 0, "case_sensitive"));
        object byValue = Argument(arguments, keywords, 1, "by");
        bool byKey = byValue == null || AsText(byValue) != "value";
        bool reverse = JinjaValues.IsTruthy(Argument(arguments, keywords, 2, "reverse"));

        var pairs = new List<object>();
        foreach (KeyValuePair<string, object> pair in mapping)
        {
            pairs.Add(new List<object> { pair.Key, pair.Value });
        }

        StableSort(pairs, (left, right) =>
        {
            object a = ((IList<object>)left)[byKey ? 0 : 1];
            object b = ((IList<object>)right)[byKey ? 0 : 1];
            if (!caseSensitive && a is string leftText && b is string rightText)
            {
                return string.CompareOrdinal(leftText.ToLowerInvariant(), rightText.ToLowerInvariant());
            }

            return JinjaValues.Compare(a, b);
        });

        if (reverse)
        {
            pairs.Reverse();
        }

        return pairs;
    }

    private static object Sum(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        object attribute = Argument(arguments, keywords, 0, "attribute");
        object total = Argument(arguments, keywords, 1, "start") ?? 0L;
        foreach (object item in JinjaValues.Iterate(value))
        {
            total = JinjaValues.Arithmetic(
                JinjaOperator.Add, total, attribute == null ? item : ReadAttributePath(item, attribute));
        }

        return total;
    }

    private static object Extremum(
        object value, IList<object> arguments, IDictionary<string, object> keywords, bool smallest)
    {
        object attribute = Argument(arguments, keywords, 1, "attribute");
        object best = null;
        object bestKey = null;
        bool seen = false;
        foreach (object item in JinjaValues.Iterate(value))
        {
            object key = attribute == null ? item : ReadAttributePath(item, attribute);
            if (!seen)
            {
                seen = true;
                best = item;
                bestKey = key;
                continue;
            }

            int order = JinjaValues.Compare(key, bestKey);
            if (smallest ? order < 0 : order > 0)
            {
                best = item;
                bestKey = key;
            }
        }

        return seen ? best : JinjaUndefined.Instance;
    }

    private static object Round(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        if (!JinjaValues.TryGetNumber(value, out double number, out bool _))
        {
            throw JinjaValues.RuntimeError("'round' needs a number.");
        }

        object precisionValue = Argument(arguments, keywords, 0, "precision");
        int precision = precisionValue == null || precisionValue is JinjaUndefined
            ? 0
            : JinjaValues.ToIndex(precisionValue, "precision");
        object methodValue = Argument(arguments, keywords, 1, "method");
        string method = methodValue == null || methodValue is JinjaUndefined ? "common" : AsText(methodValue);
        double factor = Math.Pow(10, precision);
        switch (method)
        {
            case "ceil":
                return Math.Ceiling(number * factor) / factor;
            case "floor":
                return Math.Floor(number * factor) / factor;
            case "common":
                // Jinja's 'common' method is Python's built-in round(), which rounds a halfway value
                // to the nearest EVEN digit - round(0.5) is 0 and round(2.5) is 2, not 1 and 3.
                return CommonRound(number, precision);
            default:
                throw JinjaValues.RuntimeError("'round' method must be 'common', 'ceil' or 'floor'.");
        }
    }

    private static double CommonRound(double number, int precision)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return number;
        }

        if (precision < 0)
        {
            double factor = Math.Pow(10, -precision);
            return Math.Round(number / factor, MidpointRounding.ToEven) * factor;
        }

        if (precision > 15 || Math.Abs(number) >= 1e15)
        {
            return number;
        }

        // Python's round() rounds the EXACT binary value of the double: 2.675 is really
        // 2.674999999999999822..., so round(2.675, 2) is 2.67. Scaling the double by a power of ten
        // first - which is what Math.Round(double, int, ...) does - lands on 2.68 instead, so the
        // shortest round-tripping decimal form is rounded as a decimal here.
        decimal exact = decimal.Parse(
            number.ToString("G17", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
        return (double)Math.Round(exact, precision, MidpointRounding.ToEven);
    }

    private static void StableSort(List<object> items, Comparison<object> comparison)
    {
        // List<T>.Sort is an unstable introsort; Python's sorted() and Jinja's sort/dictsort filters
        // are stable, so the original position breaks every tie.
        int count = items.Count;
        var order = new int[count];
        var source = new object[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
            source[i] = items[i];
        }

        Array.Sort(order, (left, right) =>
        {
            int result = comparison(source[left], source[right]);
            return result != 0 ? result : left.CompareTo(right);
        });

        for (int i = 0; i < count; i++)
        {
            items[i] = source[order[i]];
        }
    }

    private static object Batch(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        int size = JinjaValues.ToIndex(Argument(arguments, keywords, 0, "linecount"), "linecount");
        object fill = Argument(arguments, keywords, 1, "fill_with");
        var result = new List<object>();
        var current = new List<object>();
        foreach (object item in JinjaValues.Iterate(value))
        {
            current.Add(item);
            if (current.Count != size)
            {
                continue;
            }

            result.Add(current);
            current = new List<object>();
        }

        if (current.Count > 0)
        {
            if (fill != null)
            {
                while (current.Count < size)
                {
                    current.Add(fill);
                }
            }

            result.Add(current);
        }

        return result;
    }

    private static object SliceInto(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        int slices = JinjaValues.ToIndex(Argument(arguments, keywords, 0, "slices"), "slices");
        if (slices <= 0)
        {
            throw JinjaValues.RuntimeError("'slice' needs a positive slice count.");
        }

        object fill = Argument(arguments, keywords, 1, "fill_with");
        IList<object> items = JinjaValues.Iterate(value);
        int perSlice = items.Count / slices;
        int remainder = items.Count % slices;
        var result = new List<object>();
        int offset = 0;
        for (int i = 0; i < slices; i++)
        {
            int take = perSlice + (i < remainder ? 1 : 0);
            var current = new List<object>();
            for (int j = 0; j < take; j++)
            {
                current.Add(items[offset++]);
            }

            if (fill != null && i >= remainder && remainder > 0)
            {
                current.Add(fill);
            }

            result.Add(current);
        }

        return result;
    }

    /// <summary>Centres text in a field, as Python's <c>str.center</c> does.</summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The field width.</param>
    /// <param name="fill">The fill character.</param>
    /// <returns>The padded text.</returns>
    internal static string Center(string text, int width, char fill)
    {
        if (text.Length >= width)
        {
            return text;
        }

        int total = width - text.Length;
        int left = (total / 2) + (total & width & 1);
        return new string(fill, left) + text + new string(fill, total - left);
    }

    private static object UrlEncode(object value)
    {
        if (value is IDictionary<string, object> mapping)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, object> pair in mapping)
            {
                parts.Add(EncodeComponent(pair.Key) + "=" + EncodeComponent(AsText(pair.Value)));
            }

            return string.Join("&", parts);
        }

        return EncodeComponent(AsText(value));
    }

    private static string EncodeComponent(string text)
    {
        var builder = new StringBuilder();
        foreach (byte octet in Encoding.UTF8.GetBytes(text))
        {
            char character = (char)octet;
            if (char.IsLetterOrDigit(character) && octet < 128)
            {
                builder.Append(character);
            }
            else if (character == '-' || character == '_' || character == '.' || character == '~'
                || character == '/')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('%').Append(octet.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static object GroupBy(object value, IList<object> arguments, IDictionary<string, object> keywords)
    {
        object attribute = Argument(arguments, keywords, 0, "attribute");
        if (attribute == null)
        {
            throw JinjaValues.RuntimeError("'groupby' needs an attribute name.");
        }

        var keys = new List<object>();
        var groups = new List<List<object>>();
        foreach (object item in JinjaValues.Iterate(value))
        {
            object key = ReadAttributePath(item, attribute);
            int found = -1;
            for (int i = 0; i < keys.Count; i++)
            {
                if (JinjaValues.AreEqual(keys[i], key))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                keys.Add(key);
                groups.Add(new List<object> { item });
                continue;
            }

            groups[found].Add(item);
        }

        var result = new List<object>();
        for (int i = 0; i < keys.Count; i++)
        {
            IDictionary<string, object> group = JinjaValues.NewMapping();
            group["grouper"] = keys[i];
            group["list"] = groups[i];
            result.Add(group);
        }

        return result;
    }

    /// <summary>Applies Python's <c>%</c> string formatting for the <c>format</c> filter.</summary>
    /// <param name="format">The format string.</param>
    /// <param name="value">A single value or a list of values.</param>
    /// <returns>The formatted text.</returns>
    internal static string PercentFormat(string format, object value)
    {
        IList<object> values = value is IList<object> list ? list : new List<object> { value };
        var builder = new StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                builder.Append(format[i]);
                continue;
            }

            int cursor = i + 1;
            bool leftAlign = false;
            bool alwaysSign = false;
            bool blankSign = false;
            bool alternate = false;
            bool zeroPad = false;
            while (cursor < format.Length)
            {
                char flag = format[cursor];
                if (flag == '-')
                {
                    leftAlign = true;
                }
                else if (flag == '+')
                {
                    alwaysSign = true;
                }
                else if (flag == ' ')
                {
                    blankSign = true;
                }
                else if (flag == '#')
                {
                    alternate = true;
                }
                else if (flag == '0')
                {
                    zeroPad = true;
                }
                else
                {
                    break;
                }

                cursor++;
            }

            int width = 0;
            while (cursor < format.Length && char.IsDigit(format[cursor]))
            {
                width = (width * 10) + (format[cursor] - '0');
                cursor++;
            }

            int precision = -1;
            if (cursor < format.Length && format[cursor] == '.')
            {
                cursor++;
                precision = 0;
                while (cursor < format.Length && char.IsDigit(format[cursor]))
                {
                    precision = (precision * 10) + (format[cursor] - '0');
                    cursor++;
                }
            }

            if (cursor >= format.Length)
            {
                builder.Append(format, i, format.Length - i);
                break;
            }

            char conversion = format[cursor];
            if (conversion == '%')
            {
                builder.Append('%');
                i = cursor;
                continue;
            }

            object current = next < values.Count ? values[next] : null;
            string body = ConvertPercentValue(
                conversion, current, precision, alwaysSign, blankSign, alternate, out bool numeric);
            if (body == null)
            {
                builder.Append(format, i, (cursor - i) + 1);
                i = cursor;
                continue;
            }

            next++;
            builder.Append(PadPercentValue(body, width, leftAlign, zeroPad && numeric));
            i = cursor;
        }

        return builder.ToString();
    }

    private static string ConvertPercentValue(
        char conversion,
        object value,
        int precision,
        bool alwaysSign,
        bool blankSign,
        bool alternate,
        out bool numeric)
    {
        numeric = true;
        switch (conversion)
        {
            case 's':
            case 'r':
                {
                    numeric = false;
                    string text = conversion == 's'
                        ? JinjaValues.ToDisplayString(value)
                        : JinjaValues.ToRepresentation(value);
                    return precision >= 0 && precision < text.Length ? text.Substring(0, precision) : text;
                }

            case 'c':
                {
                    numeric = false;
                    if (JinjaValues.TryGetInteger(value, out long code) && code >= 0L && code <= 0x10FFFFL)
                    {
                        return char.ConvertFromUtf32((int)code);
                    }

                    return JinjaValues.ToDisplayString(value);
                }

            case 'd':
            case 'i':
            case 'u':
                {
                    if (!JinjaValues.TryGetNumber(value, out double number, out bool _))
                    {
                        numeric = false;
                        return JinjaValues.ToDisplayString(value);
                    }

                    long whole = JinjaValues.TryGetInteger(value, out long exact) ? exact : (long)number;
                    return SignPercentValue(
                        whole < 0L, Math.Abs(whole).ToString(CultureInfo.InvariantCulture), alwaysSign, blankSign);
                }

            case 'x':
            case 'X':
            case 'o':
                {
                    if (!JinjaValues.TryGetInteger(value, out long whole))
                    {
                        numeric = false;
                        return JinjaValues.ToDisplayString(value);
                    }

                    long magnitude = Math.Abs(whole);
                    string digits = conversion == 'o'
                        ? Convert.ToString(magnitude, 8)
                        : magnitude.ToString(conversion == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                    if (alternate)
                    {
                        digits = conversion == 'o' ? "0o" + digits : (conversion == 'x' ? "0x" : "0X") + digits;
                    }

                    return SignPercentValue(whole < 0L, digits, alwaysSign, blankSign);
                }

            case 'f':
            case 'F':
            case 'e':
            case 'E':
            case 'g':
            case 'G':
                {
                    if (!JinjaValues.TryGetNumber(value, out double number, out bool _))
                    {
                        numeric = false;
                        return JinjaValues.ToDisplayString(value);
                    }

                    int places = precision < 0 ? 6 : precision;
                    string digits;
                    if (conversion == 'f' || conversion == 'F')
                    {
                        digits = Math.Abs(number).ToString(
                            "F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }
                    else if (conversion == 'e' || conversion == 'E')
                    {
                        digits = FormatExponential(Math.Abs(number), places, conversion == 'E');
                    }
                    else
                    {
                        digits = Math.Abs(number).ToString(
                            "G" + Math.Max(1, places).ToString(CultureInfo.InvariantCulture),
                            CultureInfo.InvariantCulture);
                        if (conversion == 'g')
                        {
                            digits = digits.ToLowerInvariant();
                        }
                    }

                    return SignPercentValue(number < 0d, digits, alwaysSign, blankSign);
                }

            default:
                numeric = false;
                return null;
        }
    }

    private static string FormatExponential(double magnitude, int places, bool upper)
    {
        string text = magnitude.ToString(
            (upper ? "E" : "e") + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        int marker = text.IndexOfAny(new[] { 'e', 'E' });
        if (marker < 0)
        {
            return text;
        }

        string mantissa = text.Substring(0, marker);
        string exponent = text.Substring(marker + 1);
        char sign = '+';
        if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
        {
            sign = exponent[0];
            exponent = exponent.Substring(1);
        }

        exponent = exponent.TrimStart('0');
        if (exponent.Length < 2)
        {
            exponent = exponent.PadLeft(2, '0');
        }

        return mantissa + text[marker] + sign + exponent;
    }

    private static string SignPercentValue(bool negative, string digits, bool alwaysSign, bool blankSign)
    {
        if (negative)
        {
            return "-" + digits;
        }

        if (alwaysSign)
        {
            return "+" + digits;
        }

        return blankSign ? " " + digits : digits;
    }

    private static string PadPercentValue(string body, int width, bool leftAlign, bool zeroPad)
    {
        if (body.Length >= width)
        {
            return body;
        }

        if (leftAlign)
        {
            return body.PadRight(width);
        }

        if (!zeroPad)
        {
            return body.PadLeft(width);
        }

        int at = 0;
        if (body.Length > 0 && (body[0] == '-' || body[0] == '+' || body[0] == ' '))
        {
            at = 1;
        }

        return body.Substring(0, at) + body.Substring(at).PadLeft(width - at, '0');
    }

    /// <summary>Applies Python's <c>str.format</c> for the positional and named forms.</summary>
    /// <param name="format">The format string.</param>
    /// <param name="arguments">The positional arguments.</param>
    /// <param name="keywords">The keyword arguments.</param>
    /// <returns>The formatted text.</returns>
    internal static string BraceFormat(
        string format, IList<object> arguments, IDictionary<string, object> keywords)
    {
        var builder = new StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            char character = format[i];
            if (character == '{' && i + 1 < format.Length && format[i + 1] == '{')
            {
                builder.Append('{');
                i++;
                continue;
            }

            if (character == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                builder.Append('}');
                i++;
                continue;
            }

            if (character != '{')
            {
                builder.Append(character);
                continue;
            }

            int close = format.IndexOf('}', i);
            if (close < 0)
            {
                throw JinjaValues.RuntimeError("unmatched '{' in a format string.");
            }

            string field = format.Substring(i + 1, close - i - 1);
            i = close;
            int colon = field.IndexOf(':');
            if (colon >= 0)
            {
                field = field.Substring(0, colon);
            }

            object value;
            if (field.Length == 0)
            {
                value = arguments != null && next < arguments.Count ? arguments[next++] : null;
            }
            else if (long.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out long position))
            {
                value = arguments != null && position < arguments.Count ? arguments[(int)position] : null;
            }
            else
            {
                value = keywords != null && keywords.TryGetValue(field, out object named) ? named : null;
            }

            builder.Append(JinjaValues.ToDisplayString(value));
        }

        return builder.ToString();
    }
}
