using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The Python methods a template may call on a string, a list or a mapping. Only the ones chat
/// templates actually reach for are here, and each follows CPython's behaviour for the arguments it
/// accepts.
/// </summary>
internal static class JinjaMethods
{
    private static readonly HashSet<string> StringMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "strip", "lstrip", "rstrip", "startswith", "endswith", "split", "rsplit", "replace", "lower",
        "upper", "title", "capitalize", "find", "rfind", "count", "join", "format", "index", "isalpha",
        "isdigit", "isspace", "isupper", "islower", "zfill", "ljust", "rjust", "center", "partition",
        "rpartition", "splitlines", "removeprefix", "removesuffix",
    };

    private static readonly HashSet<string> ListMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "append", "extend", "pop", "insert", "remove", "index", "count", "copy", "clear", "reverse",
    };

    private static readonly HashSet<string> MappingMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "items", "keys", "values", "get", "update", "pop", "setdefault", "copy", "clear",
    };

    /// <summary>Reports whether a name is one of the supported string methods.</summary>
    /// <param name="name">The method name.</param>
    /// <returns>True when it is supported.</returns>
    internal static bool IsStringMethod(string name) => StringMethods.Contains(name);

    /// <summary>Reports whether a name is one of the supported list methods.</summary>
    /// <param name="name">The method name.</param>
    /// <returns>True when it is supported.</returns>
    internal static bool IsListMethod(string name) => ListMethods.Contains(name);

    /// <summary>Reports whether a name is one of the supported mapping methods.</summary>
    /// <param name="name">The method name.</param>
    /// <returns>True when it is supported.</returns>
    internal static bool IsMappingMethod(string name) => MappingMethods.Contains(name);

    /// <summary>Invokes a bound method.</summary>
    /// <param name="method">The bound method.</param>
    /// <param name="arguments">The positional arguments.</param>
    /// <param name="keywords">The keyword arguments, or null.</param>
    /// <returns>The result.</returns>
    internal static object Invoke(
        JinjaBoundMethod method, IList<object> arguments, IDictionary<string, object> keywords)
    {
        switch (method.Target)
        {
            case JinjaLoop loop:
                if (method.Name == "cycle")
                {
                    return loop.Cycle(arguments);
                }

                if (method.Name == "changed")
                {
                    return loop.Changed(Argument(arguments, 0));
                }

                throw JinjaValues.RuntimeError("unknown method '" + method.Name + "'.");
            case string text:
                return InvokeString(text, method.Name, arguments, keywords);
            case IDictionary<string, object> mapping:
                return InvokeMapping(mapping, method.Name, arguments);
            case IList<object> list:
                return InvokeList(list, method.Name, arguments);
            default:
                throw JinjaValues.RuntimeError(
                    "'" + method.Name + "' is not callable on " + JinjaValues.DescribeType(method.Target) + ".");
        }
    }

    private static object Argument(IList<object> arguments, int index)
    {
        return arguments != null && index < arguments.Count ? arguments[index] : null;
    }

    private static string TextArgument(IList<object> arguments, int index, string fallback)
    {
        object value = Argument(arguments, index);
        return value == null || value is JinjaUndefined ? fallback : JinjaValues.ToDisplayString(value);
    }

    private static object InvokeString(
        string text, string name, IList<object> arguments, IDictionary<string, object> keywords)
    {
        switch (name)
        {
            case "strip":
                return Trim(text, Argument(arguments, 0), true, true);
            case "lstrip":
                return Trim(text, Argument(arguments, 0), true, false);
            case "rstrip":
                return Trim(text, Argument(arguments, 0), false, true);
            case "startswith":
                return MatchesEnd(text, Argument(arguments, 0), true);
            case "endswith":
                return MatchesEnd(text, Argument(arguments, 0), false);
            case "split":
                return Split(text, Argument(arguments, 0), MaxSplit(arguments), false);
            case "rsplit":
                return Split(text, Argument(arguments, 0), MaxSplit(arguments), true);
            case "replace":
                return Replace(text, arguments);
            case "lower":
                return text.ToLowerInvariant();
            case "upper":
                return text.ToUpperInvariant();
            case "title":
                return Title(text);
            case "capitalize":
                return Capitalize(text);
            case "find":
                return (long)text.IndexOf(TextArgument(arguments, 0, string.Empty), StringComparison.Ordinal);
            case "rfind":
                return (long)text.LastIndexOf(TextArgument(arguments, 0, string.Empty), StringComparison.Ordinal);
            case "index":
                {
                    int position = text.IndexOf(TextArgument(arguments, 0, string.Empty), StringComparison.Ordinal);
                    if (position < 0)
                    {
                        throw JinjaValues.RuntimeError("substring not found.");
                    }

                    return (long)position;
                }

            case "count":
                return (long)CountOccurrences(text, TextArgument(arguments, 0, string.Empty));
            case "join":
                return Join(text, Argument(arguments, 0));
            case "format":
                return JinjaFilters.BraceFormat(text, arguments, keywords);
            case "isalpha":
                return text.Length > 0 && AllCharacters(text, char.IsLetter);
            case "isdigit":
                return text.Length > 0 && AllCharacters(text, char.IsDigit);
            case "isspace":
                return text.Length > 0 && AllCharacters(text, char.IsWhiteSpace);
            case "isupper":
                return HasCased(text) && text == text.ToUpperInvariant();
            case "islower":
                return HasCased(text) && text == text.ToLowerInvariant();
            case "zfill":
                return text.PadLeft(JinjaValues.ToIndex(Argument(arguments, 0), "zfill width"), '0');
            case "ljust":
                return text.PadRight(
                    JinjaValues.ToIndex(Argument(arguments, 0), "ljust width"), FillCharacter(arguments));
            case "rjust":
                return text.PadLeft(
                    JinjaValues.ToIndex(Argument(arguments, 0), "rjust width"), FillCharacter(arguments));
            case "center":
                return JinjaFilters.Center(
                    text, JinjaValues.ToIndex(Argument(arguments, 0), "center width"), FillCharacter(arguments));
            case "partition":
                return Partition(text, TextArgument(arguments, 0, string.Empty), false);
            case "rpartition":
                return Partition(text, TextArgument(arguments, 0, string.Empty), true);
            case "splitlines":
                {
                    var lines = new List<object>();
                    if (text.Length == 0)
                    {
                        return lines;
                    }

                    foreach (string line in text.Split('\n'))
                    {
                        lines.Add(line.EndsWith("\r", StringComparison.Ordinal)
                            ? line.Substring(0, line.Length - 1)
                            : line);
                    }

                    if (lines.Count > 0 && (string)lines[lines.Count - 1] == string.Empty
                        && text.Length > 0)
                    {
                        lines.RemoveAt(lines.Count - 1);
                    }

                    return lines;
                }

            case "removeprefix":
                {
                    string prefix = TextArgument(arguments, 0, string.Empty);
                    return prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal)
                        ? text.Substring(prefix.Length)
                        : text;
                }

            case "removesuffix":
                {
                    string suffix = TextArgument(arguments, 0, string.Empty);
                    return suffix.Length > 0 && text.EndsWith(suffix, StringComparison.Ordinal)
                        ? text.Substring(0, text.Length - suffix.Length)
                        : text;
                }

            default:
                throw JinjaValues.RuntimeError("unknown method '" + name + "'.");
        }
    }

    private static char FillCharacter(IList<object> arguments)
    {
        string fill = TextArgument(arguments, 1, " ");
        return fill.Length > 0 ? fill[0] : ' ';
    }

    private static int MaxSplit(IList<object> arguments)
    {
        object value = Argument(arguments, 1);
        return value == null || value is JinjaUndefined ? -1 : JinjaValues.ToIndex(value, "maxsplit");
    }

    private static bool AllCharacters(string text, Func<char, bool> predicate)
    {
        foreach (char character in text)
        {
            if (!predicate(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCased(string text)
    {
        foreach (char character in text)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountOccurrences(string text, string needle)
    {
        if (needle.Length == 0)
        {
            return text.Length + 1;
        }

        int count = 0;
        int position = 0;
        while (true)
        {
            int found = text.IndexOf(needle, position, StringComparison.Ordinal);
            if (found < 0)
            {
                return count;
            }

            count++;
            position = found + needle.Length;
        }
    }

    /// <summary>Applies Python's <c>strip</c>, <c>lstrip</c> and <c>rstrip</c>.</summary>
    /// <param name="text">The text to trim.</param>
    /// <param name="characters">The characters to remove, or null for whitespace.</param>
    /// <param name="fromStart">True to trim the start.</param>
    /// <param name="fromEnd">True to trim the end.</param>
    /// <returns>The trimmed text.</returns>
    internal static string Trim(string text, object characters, bool fromStart, bool fromEnd)
    {
        if (characters == null || characters is JinjaUndefined)
        {
            if (fromStart && fromEnd)
            {
                return text.Trim();
            }

            return fromStart ? text.TrimStart() : text.TrimEnd();
        }

        char[] set = JinjaValues.ToDisplayString(characters).ToCharArray();
        if (fromStart && fromEnd)
        {
            return text.Trim(set);
        }

        return fromStart ? text.TrimStart(set) : text.TrimEnd(set);
    }

    private static bool MatchesEnd(string text, object candidate, bool atStart)
    {
        if (candidate is IList<object> options)
        {
            foreach (object option in options)
            {
                if (MatchesEnd(text, option, atStart))
                {
                    return true;
                }
            }

            return false;
        }

        string needle = JinjaValues.ToDisplayString(candidate);
        return atStart
            ? text.StartsWith(needle, StringComparison.Ordinal)
            : text.EndsWith(needle, StringComparison.Ordinal);
    }

    private static object Split(string text, object separator, int maximum, bool fromEnd)
    {
        var parts = new List<object>();
        if (separator == null || separator is JinjaUndefined)
        {
            if (fromEnd && maximum >= 0)
            {
                return SplitWhitespaceFromEnd(text, maximum);
            }

            string[] pieces = text.Split(
                (char[])null, maximum < 0 ? int.MaxValue : maximum + 1, StringSplitOptions.RemoveEmptyEntries);
            foreach (string piece in pieces)
            {
                parts.Add(piece);
            }

            return parts;
        }

        string delimiter = JinjaValues.ToDisplayString(separator);
        if (delimiter.Length == 0)
        {
            throw JinjaValues.RuntimeError("empty separator.");
        }

        if (maximum < 0)
        {
            foreach (string piece in text.Split(new[] { delimiter }, StringSplitOptions.None))
            {
                parts.Add(piece);
            }

            return parts;
        }

        var collected = new List<string>();
        string rest = text;
        while (collected.Count < maximum)
        {
            int position = fromEnd
                ? rest.LastIndexOf(delimiter, StringComparison.Ordinal)
                : rest.IndexOf(delimiter, StringComparison.Ordinal);
            if (position < 0)
            {
                break;
            }

            if (fromEnd)
            {
                collected.Insert(0, rest.Substring(position + delimiter.Length));
                rest = rest.Substring(0, position);
            }
            else
            {
                collected.Add(rest.Substring(0, position));
                rest = rest.Substring(position + delimiter.Length);
            }
        }

        if (fromEnd)
        {
            collected.Insert(0, rest);
        }
        else
        {
            collected.Add(rest);
        }

        foreach (string piece in collected)
        {
            parts.Add(piece);
        }

        return parts;
    }

    private static string ReplaceEmpty(string text, string newValue, object count)
    {
        // CPython treats an empty needle as matching at every gap, so "ab".replace("", "-") is "-a-b-".
        int limit = text.Length + 1;
        if (count != null && !(count is JinjaUndefined))
        {
            int requested = JinjaValues.ToIndex(count, "replace count");
            if (requested >= 0)
            {
                limit = Math.Min(requested, limit);
            }
        }

        if (limit <= 0)
        {
            return text;
        }

        var builder = new StringBuilder();
        int inserted = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (inserted < limit)
            {
                builder.Append(newValue);
                inserted++;
            }

            if (i < text.Length)
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    private static IList<object> SplitWhitespaceFromEnd(string text, int maximum)
    {
        // CPython's rsplit(None, n) takes its pieces off the END, and the remainder keeps whatever
        // leading whitespace it had while losing the whitespace that separated it from the last piece.
        var collected = new List<object>();
        int end = text.Length;
        while (collected.Count < maximum)
        {
            int stop = end;
            while (stop > 0 && char.IsWhiteSpace(text[stop - 1]))
            {
                stop--;
            }

            if (stop == 0)
            {
                end = 0;
                break;
            }

            int start = stop;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            {
                start--;
            }

            collected.Insert(0, text.Substring(start, stop - start));
            end = start;
            if (start == 0)
            {
                break;
            }
        }

        string rest = text.Substring(0, end).TrimEnd();
        if (rest.Length > 0)
        {
            collected.Insert(0, rest);
        }

        return collected;
    }

    private static object Replace(string text, IList<object> arguments)
    {
        string oldValue = TextArgument(arguments, 0, string.Empty);
        string newValue = TextArgument(arguments, 1, string.Empty);
        object count = Argument(arguments, 2);
        if (oldValue.Length == 0)
        {
            return ReplaceEmpty(text, newValue, count);
        }

        if (count == null || count is JinjaUndefined)
        {
            return text.Replace(oldValue, newValue);
        }

        int limit = JinjaValues.ToIndex(count, "replace count");
        if (limit < 0)
        {
            return text.Replace(oldValue, newValue);
        }

        var builder = new StringBuilder();
        int position = 0;
        while (limit > 0)
        {
            int found = text.IndexOf(oldValue, position, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            builder.Append(text, position, found - position).Append(newValue);
            position = found + oldValue.Length;
            limit--;
        }

        return builder.Append(text.Substring(position)).ToString();
    }

    private static string Title(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool previousWasLetter = false;
        foreach (char character in text)
        {
            bool isLetter = char.IsLetter(character);
            if (!isLetter)
            {
                builder.Append(character);
            }
            else if (previousWasLetter)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(char.ToUpperInvariant(character));
            }

            previousWasLetter = isLetter;
        }

        return builder.ToString();
    }

    private static string Capitalize(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        return char.ToUpperInvariant(text[0]) + text.Substring(1).ToLowerInvariant();
    }

    private static object Join(string separator, object value)
    {
        var builder = new StringBuilder();
        bool first = true;
        foreach (object item in JinjaValues.Iterate(value))
        {
            if (!first)
            {
                builder.Append(separator);
            }

            first = false;
            builder.Append(JinjaValues.ToDisplayString(item));
        }

        return builder.ToString();
    }

    private static object Partition(string text, string separator, bool fromEnd)
    {
        int position = fromEnd
            ? text.LastIndexOf(separator, StringComparison.Ordinal)
            : text.IndexOf(separator, StringComparison.Ordinal);
        if (separator.Length == 0 || position < 0)
        {
            return fromEnd
                ? new List<object> { string.Empty, string.Empty, text }
                : new List<object> { text, string.Empty, string.Empty };
        }

        return new List<object>
        {
            text.Substring(0, position),
            separator,
            text.Substring(position + separator.Length),
        };
    }

    private static object InvokeList(IList<object> list, string name, IList<object> arguments)
    {
        switch (name)
        {
            case "append":
                list.Add(Argument(arguments, 0));
                return null;
            case "extend":
                foreach (object item in JinjaValues.Iterate(Argument(arguments, 0)))
                {
                    list.Add(item);
                }

                return null;
            case "pop":
                {
                    if (list.Count == 0)
                    {
                        throw JinjaValues.RuntimeError("pop from an empty list.");
                    }

                    object index = Argument(arguments, 0);
                    int position = index == null || index is JinjaUndefined
                        ? list.Count - 1
                        : JinjaValues.ToIndex(index, "list index");
                    if (position < 0)
                    {
                        position += list.Count;
                    }

                    if (position < 0 || position >= list.Count)
                    {
                        throw JinjaValues.RuntimeError("pop index out of range.");
                    }

                    object removed = list[position];
                    list.RemoveAt(position);
                    return removed;
                }

            case "insert":
                {
                    int position = JinjaValues.ToIndex(Argument(arguments, 0), "list index");
                    if (position < 0)
                    {
                        position += list.Count;
                    }

                    position = Math.Max(0, Math.Min(position, list.Count));
                    list.Insert(position, Argument(arguments, 1));
                    return null;
                }

            case "remove":
                for (int i = 0; i < list.Count; i++)
                {
                    if (JinjaValues.AreEqual(list[i], Argument(arguments, 0)))
                    {
                        list.RemoveAt(i);
                        return null;
                    }
                }

                throw JinjaValues.RuntimeError("the value is not in the list.");
            case "index":
                for (int i = 0; i < list.Count; i++)
                {
                    if (JinjaValues.AreEqual(list[i], Argument(arguments, 0)))
                    {
                        return (long)i;
                    }
                }

                throw JinjaValues.RuntimeError("the value is not in the list.");
            case "count":
                {
                    long found = 0;
                    foreach (object item in list)
                    {
                        if (JinjaValues.AreEqual(item, Argument(arguments, 0)))
                        {
                            found++;
                        }
                    }

                    return found;
                }

            case "copy":
                return new List<object>(list);
            case "reverse":
                {
                    var reversed = new List<object>(list);
                    reversed.Reverse();
                    list.Clear();
                    foreach (object item in reversed)
                    {
                        list.Add(item);
                    }

                    return null;
                }

            case "clear":
                list.Clear();
                return null;
            default:
                throw JinjaValues.RuntimeError("unknown method '" + name + "'.");
        }
    }

    private static object InvokeMapping(
        IDictionary<string, object> mapping, string name, IList<object> arguments)
    {
        switch (name)
        {
            case "items":
                {
                    var items = new List<object>(mapping.Count);
                    foreach (KeyValuePair<string, object> pair in mapping)
                    {
                        items.Add(new List<object> { pair.Key, pair.Value });
                    }

                    return items;
                }

            case "keys":
                {
                    var keys = new List<object>(mapping.Count);
                    foreach (KeyValuePair<string, object> pair in mapping)
                    {
                        keys.Add(pair.Key);
                    }

                    return keys;
                }

            case "values":
                {
                    var values = new List<object>(mapping.Count);
                    foreach (KeyValuePair<string, object> pair in mapping)
                    {
                        values.Add(pair.Value);
                    }

                    return values;
                }

            case "get":
                {
                    string key = TextArgument(arguments, 0, string.Empty);
                    return mapping.TryGetValue(key, out object value) ? value : Argument(arguments, 1);
                }

            case "update":
                {
                    object source = Argument(arguments, 0);
                    if (source is IDictionary<string, object> other)
                    {
                        foreach (KeyValuePair<string, object> pair in other)
                        {
                            mapping[pair.Key] = pair.Value;
                        }
                    }

                    return null;
                }

            case "pop":
                {
                    string key = TextArgument(arguments, 0, string.Empty);
                    if (mapping.TryGetValue(key, out object value))
                    {
                        mapping.Remove(key);
                        return value;
                    }

                    if (arguments != null && arguments.Count > 1)
                    {
                        return arguments[1];
                    }

                    throw JinjaValues.RuntimeError("key '" + key + "' is not in the mapping.");
                }

            case "setdefault":
                {
                    string key = TextArgument(arguments, 0, string.Empty);
                    if (mapping.TryGetValue(key, out object value))
                    {
                        return value;
                    }

                    object fallback = Argument(arguments, 1);
                    mapping[key] = fallback;
                    return fallback;
                }

            case "copy":
                {
                    IDictionary<string, object> copy = JinjaValues.NewMapping();
                    foreach (KeyValuePair<string, object> pair in mapping)
                    {
                        copy[pair.Key] = pair.Value;
                    }

                    return copy;
                }

            case "clear":
                mapping.Clear();
                return null;
            default:
                throw JinjaValues.RuntimeError("unknown method '" + name + "'.");
        }
    }

}
