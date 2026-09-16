// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/funcs.go (BSD-3-Clause);

/// <summary>
/// Go's built-in template functions. <c>and</c> and <c>or</c> are listed here for arity checking but are
/// evaluated by the executor, because they have to short-circuit before their arguments are evaluated.
/// </summary>
internal static class GoFuncs
{
    private const string BadComparisonType = "invalid type for comparison";

    private static readonly Dictionary<string, GoFunction> BuiltinTable = Build();

    /// <summary>The built-in function table, keyed by name.</summary>
    internal static IReadOnlyDictionary<string, GoFunction> Builtins => BuiltinTable;

    /// <summary>
    /// Compares two values the way Go's <c>eq</c> does for one pair.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    /// <exception cref="ChatTemplateException">The values cannot be compared.</exception>
    internal static bool AreEqual(object left, object right)
    {
        GoKind k1 = BasicKind(left);
        GoKind k2 = BasicKind(right);
        if (k1 != k2)
        {
            if (k1 == GoKind.Int && k2 == GoKind.Uint)
            {
                long i = GoValue.ToInt64(left);
                return i >= 0 && (ulong)i == GoValue.ToUInt64(right);
            }

            if (k1 == GoKind.Uint && k2 == GoKind.Int)
            {
                long i = GoValue.ToInt64(right);
                return i >= 0 && GoValue.ToUInt64(left) == (ulong)i;
            }

            if (GoValue.KindOf(left) != GoKind.Invalid && GoValue.KindOf(right) != GoKind.Invalid)
            {
                throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                    "incompatible types for comparison: {0} and {1}", GoValue.TypeName(left),
                    GoValue.TypeName(right)));
            }

            return false;
        }

        switch (k1)
        {
            case GoKind.Bool:
                return (bool)left == (bool)right;
            case GoKind.Float:
                //Go compares with ==, so NaN is never equal to anything, itself included
                return GoValue.ToDouble(left) == GoValue.ToDouble(right);
            case GoKind.Int:
                return GoValue.ToInt64(left) == GoValue.ToInt64(right);
            case GoKind.String:
                return string.Equals((string)left, (string)right, StringComparison.Ordinal);
            case GoKind.Uint:
                return GoValue.ToUInt64(left) == GoValue.ToUInt64(right);
            default:
                if (GoValue.IsNil(left) || GoValue.IsNil(right))
                {
                    return GoValue.IsNil(left) == GoValue.IsNil(right);
                }

                return ReferenceEquals(left, right);
        }
    }

    private static Dictionary<string, GoFunction> Build()
    {
        Dictionary<string, GoFunction> table = new Dictionary<string, GoFunction>(StringComparer.Ordinal);
        Add(table, "and", 1, -1, args => args[args.Count - 1]);
        Add(table, "or", 1, -1, args => args[args.Count - 1]);
        Add(table, "not", 1, 1, args => !GoValue.IsTrue(args[0]));
        Add(table, "len", 1, 1, Length);
        Add(table, "index", 1, -1, Index);
        Add(table, "slice", 1, -1, Slice);
        Add(table, "print", 0, -1, args => GoFormat.Sprint(args));
        Add(table, "println", 0, -1, args => GoFormat.Sprintln(args));
        Add(table, "printf", 1, -1, Printf);
        Add(table, "html", 0, -1, args => HtmlEscape(GoFormat.Sprint(args)));
        Add(table, "js", 0, -1, args => JsEscape(GoFormat.Sprint(args)));
        Add(table, "urlquery", 0, -1, args => UrlQueryEscape(GoFormat.Sprint(args)));
        Add(table, "eq", 1, -1, Eq);
        Add(table, "ne", 2, 2, args => !AreEqual(args[0], args[1]));
        Add(table, "lt", 2, 2, args => LessThan(args[0], args[1]));
        Add(table, "le", 2, 2, args => LessThan(args[0], args[1]) || AreEqual(args[0], args[1]));
        Add(table, "gt", 2, 2, args => !(LessThan(args[0], args[1]) || AreEqual(args[0], args[1])));
        Add(table, "ge", 2, 2, args => !LessThan(args[0], args[1]));
        return table;
    }

    private static void Add(Dictionary<string, GoFunction> table, string name, int minArgs, int maxArgs,
        GoTemplateFunc invoke)
        => table[name] = new GoFunction(name, minArgs, maxArgs, invoke);

    private static object Eq(IReadOnlyList<object> args)
    {
        if (args.Count < 2)
        {
            throw new ChatTemplateException("missing argument for comparison");
        }

        for (int i = 1; i < args.Count; i++)
        {
            if (AreEqual(args[0], args[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LessThan(object left, object right)
    {
        GoKind k1 = BasicKind(left);
        GoKind k2 = BasicKind(right);
        if (k1 == GoKind.Invalid || k2 == GoKind.Invalid)
        {
            throw new ChatTemplateException(BadComparisonType);
        }

        if (k1 != k2)
        {
            if (k1 == GoKind.Int && k2 == GoKind.Uint)
            {
                long i = GoValue.ToInt64(left);
                return i < 0 || (ulong)i < GoValue.ToUInt64(right);
            }

            if (k1 == GoKind.Uint && k2 == GoKind.Int)
            {
                long i = GoValue.ToInt64(right);
                return i >= 0 && GoValue.ToUInt64(left) < (ulong)i;
            }

            throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                "incompatible types for comparison: {0} and {1}", GoValue.TypeName(left),
                GoValue.TypeName(right)));
        }

        switch (k1)
        {
            case GoKind.Float:
                return GoValue.ToDouble(left) < GoValue.ToDouble(right);
            case GoKind.Int:
                return GoValue.ToInt64(left) < GoValue.ToInt64(right);
            case GoKind.String:
                return string.CompareOrdinal((string)left, (string)right) < 0;
            case GoKind.Uint:
                return GoValue.ToUInt64(left) < GoValue.ToUInt64(right);
            default:
                throw new ChatTemplateException(BadComparisonType);
        }
    }

    private static GoKind BasicKind(object value)
    {
        GoKind kind = GoValue.KindOf(value);
        switch (kind)
        {
            case GoKind.Bool:
            case GoKind.Int:
            case GoKind.Uint:
            case GoKind.Float:
            case GoKind.String:
                return kind;
            default:
                return GoKind.Invalid;
        }
    }

    private static object Length(IReadOnlyList<object> args)
    {
        if (!GoValue.TryLength(args[0], out int length))
        {
            throw new ChatTemplateException("len of type " + GoValue.TypeName(args[0]));
        }

        return (long)length;
    }

    private static object Printf(IReadOnlyList<object> args)
    {
        if (!(args[0] is string format))
        {
            throw new ChatTemplateException("wrong type for printf format; expected string");
        }

        List<object> rest = new List<object>();
        for (int i = 1; i < args.Count; i++)
        {
            rest.Add(args[i]);
        }

        return GoFormat.Sprintf(format, rest);
    }

    private static object Index(IReadOnlyList<object> args)
    {
        object item = args[0];
        for (int i = 1; i < args.Count; i++)
        {
            object index = args[i];
            switch (GoValue.KindOf(item))
            {
                case GoKind.Slice:
                    item = ((GoSlice)item).Items[IndexArg(index, ((GoSlice)item).Items.Count, false)];
                    break;
                case GoKind.String:
                    byte[] bytes = Encoding.UTF8.GetBytes((string)item);
                    item = (long)bytes[IndexArg(index, bytes.Length, false)];
                    break;
                case GoKind.Map:
                    GoMap map = (GoMap)item;
                    if (!(index is string key))
                    {
                        throw new ChatTemplateException("value has type " + GoValue.TypeName(index)
                            + "; should be string");
                    }

                    item = map.Entries.TryGetValue(key, out object found) ? found : map.MissingValue;
                    break;
                default:
                    throw new ChatTemplateException("can't index item of type " + GoValue.TypeName(item));
            }
        }

        return item;
    }

    private static object Slice(IReadOnlyList<object> args)
    {
        object item = args[0];
        int count = args.Count - 1;
        if (count > 3)
        {
            throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                "too many slice indexes: {0}", count));
        }

        GoKind kind = GoValue.KindOf(item);
        if (kind != GoKind.Slice && kind != GoKind.String)
        {
            throw new ChatTemplateException("can't slice item of type " + GoValue.TypeName(item));
        }

        byte[] bytes = kind == GoKind.String ? Encoding.UTF8.GetBytes((string)item) : null;
        int capacity = kind == GoKind.String ? bytes.Length : ((GoSlice)item).Items.Count;
        int[] idx = { 0, capacity, capacity };
        for (int i = 0; i < count; i++)
        {
            idx[i] = IndexArg(args[i + 1], capacity, true);
        }

        if (idx[0] > idx[1])
        {
            throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                "invalid slice index: {0} > {1}", idx[0], idx[1]));
        }

        if (kind == GoKind.String)
        {
            return Encoding.UTF8.GetString(bytes, idx[0], idx[1] - idx[0]);
        }

        GoSlice source = (GoSlice)item;
        GoSlice result = new GoSlice
        {
            Stringer = source.Stringer,
            JsonMarshaler = source.JsonMarshaler,
        };
        for (int i = idx[0]; i < idx[1]; i++)
        {
            result.Items.Add(source.Items[i]);
        }

        return result;
    }

    private static int IndexArg(object index, int capacity, bool allowEnd)
    {
        GoKind kind = GoValue.KindOf(index);
        long x;
        if (kind == GoKind.Int)
        {
            x = GoValue.ToInt64(index);
        }
        else if (kind == GoKind.Uint)
        {
            x = (long)GoValue.ToUInt64(index);
        }
        else
        {
            throw new ChatTemplateException("cannot index slice/array with type " + GoValue.TypeName(index));
        }

        if (x < 0 || x > capacity || (!allowEnd && x == capacity))
        {
            throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                "index out of range: {0}", x));
        }

        return (int)x;
    }

    private static string HtmlEscape(string text)
    {
        StringBuilder builder = new StringBuilder();
        foreach (char c in text)
        {
            switch (c)
            {
                case '\0': builder.Append('�'); break;
                case '"': builder.Append("&#34;"); break;
                case '\'': builder.Append("&#39;"); break;
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    private static string JsEscape(string text)
    {
        StringBuilder builder = new StringBuilder();
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); continue;
                case '\'': builder.Append("\\'"); continue;
                case '"': builder.Append("\\\""); continue;
                case '<': builder.Append("\\u003C"); continue;
                case '>': builder.Append("\\u003E"); continue;
                case '&': builder.Append("\\u0026"); continue;
                case '=': builder.Append("\\u003D"); continue;
            }

            if (c < ' ')
            {
                builder.Append("\\u00");
                builder.Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string UrlQueryEscape(string text)
    {
        StringBuilder builder = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            char c = (char)b;
            bool unreserved = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '~';
            if (unreserved)
            {
                builder.Append(c);
            }
            else if (c == ' ')
            {
                builder.Append('+');
            }
            else
            {
                builder.Append('%');
                builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
