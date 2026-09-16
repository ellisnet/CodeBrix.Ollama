using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/encoding/json/encode.go (BSD-3-Clause);

/// <summary>
/// The encoder half of Go's <c>encoding/json</c>, to the extent a chat template can reach it through the
/// <c>json</c> function Ollama registers and through the types that render themselves as JSON. It keeps
/// Go's two habits that a hand-written encoder would get wrong: map keys come out sorted, and
/// <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> are escaped.
/// </summary>
internal static class GoJson
{
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Marshals a value to JSON text the way Go's <c>json.Marshal</c> does.
    /// </summary>
    /// <param name="value">The value to marshal.</param>
    /// <returns>The JSON text.</returns>
    internal static string Marshal(object value)
    {
        StringBuilder builder = new StringBuilder();
        Write(builder, value);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, object value)
    {
        switch (value)
        {
            case null:
            case GoUndefined _:
                builder.Append("null");
                return;
            case bool b:
                builder.Append(b ? "true" : "false");
                return;
            case string s:
                WriteString(builder, s);
                return;
            case int _:
            case long _:
                builder.Append(GoValue.ToInt64(value).ToString(CultureInfo.InvariantCulture));
                return;
            case uint _:
            case ulong _:
                builder.Append(GoValue.ToUInt64(value).ToString(CultureInfo.InvariantCulture));
                return;
            case float _:
            case double _:
                builder.Append(FormatNumber(GoValue.ToDouble(value)));
                return;
            case GoSlice slice:
                WriteSlice(builder, slice);
                return;
            case GoMap map:
                WriteMap(builder, map);
                return;
            case GoStruct structure:
                WriteStruct(builder, structure);
                return;
            default:
                WriteString(builder, value.ToString());
                return;
        }
    }

    private static void WriteSlice(StringBuilder builder, GoSlice slice)
    {
        if (slice.JsonMarshaler != null)
        {
            builder.Append(slice.JsonMarshaler(slice));
            return;
        }

        if (slice.IsNil)
        {
            builder.Append("null");
            return;
        }

        builder.Append('[');
        for (int i = 0; i < slice.Items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            Write(builder, slice.Items[i]);
        }

        builder.Append(']');
    }

    private static void WriteMap(StringBuilder builder, GoMap map)
    {
        if (map.IsNil)
        {
            builder.Append("null");
            return;
        }

        builder.Append('{');
        List<string> keys = map.SortedKeys();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            WriteString(builder, keys[i]);
            builder.Append(':');
            Write(builder, map.Entries[keys[i]]);
        }

        builder.Append('}');
    }

    private static void WriteStruct(StringBuilder builder, GoStruct structure)
    {
        builder.Append('{');
        bool first = true;
        foreach (GoStructField field in structure.Fields)
        {
            if (field.JsonName == null)
            {
                continue;
            }

            if (field.OmitEmpty && IsEmpty(field.Value))
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            WriteString(builder, field.JsonName);
            builder.Append(':');
            Write(builder, field.Value);
        }

        builder.Append('}');
    }

    private static bool IsEmpty(object value)
    {
        switch (GoValue.KindOf(value))
        {
            case GoKind.Invalid:
            case GoKind.Nil:
                return true;
            case GoKind.Bool:
                return !(bool)value;
            case GoKind.Int:
                return GoValue.ToInt64(value) == 0;
            case GoKind.Uint:
                return GoValue.ToUInt64(value) == 0;
            case GoKind.Float:
                return GoValue.ToDouble(value) == 0d;
            case GoKind.String:
                return ((string)value).Length == 0;
            case GoKind.Slice:
                return ((GoSlice)value).Items.Count == 0;
            case GoKind.Map:
                return ((GoMap)value).Entries.Count == 0;
            default:
                return false;
        }
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "null";
        }

        double absolute = Math.Abs(value);
        char format = absolute != 0 && (absolute < 1e-6 || absolute >= 1e21) ? 'e' : 'f';
        string text = GoFormat.FormatFloat(value, format);
        if (format == 'e')
        {
            //Go trims a leading zero out of the exponent, turning e-09 into e-9
            text = TrimExponentZero(text);
        }

        return text;
    }

    private static string TrimExponentZero(string text)
    {
        int n = text.Length;
        if (n >= 4 && text[n - 4] == 'e' && (text[n - 3] == '-' || text[n - 3] == '+') && text[n - 2] == '0')
        {
            return text.Substring(0, n - 2) + text[n - 1];
        }

        return text;
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value ?? string.Empty)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); continue;
                case '\\': builder.Append("\\\\"); continue;
                case '\n': builder.Append("\\n"); continue;
                case '\r': builder.Append("\\r"); continue;
                case '\t': builder.Append("\\t"); continue;
                case '<': builder.Append("\\u003c"); continue;
                case '>': builder.Append("\\u003e"); continue;
                case '&': builder.Append("\\u0026"); continue;
            }

            if (c == LineSeparator || c == ParagraphSeparator)
            {
                builder.Append("\\u");
                builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                continue;
            }

            if (c < 0x20)
            {
                builder.Append("\\u");
                builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                continue;
            }

            builder.Append(c);
        }

        builder.Append('"');
    }
}
