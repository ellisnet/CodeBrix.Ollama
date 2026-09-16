using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The JSON writer behind the <c>tojson</c> filter. It reproduces what
/// <c>json.dumps(value, ensure_ascii=False, indent=indent)</c> produces in Python, which is what the
/// Hugging Face transformers library gives a chat template: <c>", "</c> and <c>": "</c> separators
/// when there is no indent, <c>","</c> and <c>": "</c> when there is, insertion key order, and
/// non-ASCII characters left as they are.
/// </summary>
internal static class JinjaJson
{
    /// <summary>How deeply containers may nest before <c>tojson</c> refuses.</summary>
    private const int MaximumDepth = 200;

    /// <summary>Serializes a value.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="indent">The indent width, or -1 for the compact single-line form.</param>
    /// <returns>The JSON text.</returns>
    internal static string Serialize(object value, int indent)
    {
        var builder = new StringBuilder();
        Write(builder, value, indent, 0);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, object value, int indent, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw JinjaValues.RuntimeError(
                "'tojson' gave up after " + MaximumDepth.ToString(CultureInfo.InvariantCulture)
                + " levels of nesting; the value is too deep or refers to itself.");
        }

        switch (value)
        {
            case null:
            case JinjaUndefined _:
                builder.Append("null");
                return;
            case bool flag:
                builder.Append(flag ? "true" : "false");
                return;
            case long number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case double number:
                builder.Append(JinjaValues.FormatDouble(number));
                return;
            case string text:
                WriteString(builder, text);
                return;
            case IDictionary<string, object> mapping:
                WriteMapping(builder, mapping, indent, depth);
                return;
            case IList<object> list:
                WriteList(builder, list, indent, depth);
                return;
            default:
                WriteString(builder, JinjaValues.ToDisplayString(value));
                return;
        }
    }

    private static void WriteMapping(
        StringBuilder builder, IDictionary<string, object> mapping, int indent, int depth)
    {
        if (mapping.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.Append('{');
        bool first = true;
        foreach (KeyValuePair<string, object> pair in mapping)
        {
            if (!first)
            {
                builder.Append(indent < 0 ? ", " : ",");
            }

            first = false;
            AppendNewLine(builder, indent, depth + 1);
            WriteString(builder, pair.Key);
            builder.Append(": ");
            Write(builder, pair.Value, indent, depth + 1);
        }

        AppendNewLine(builder, indent, depth);
        builder.Append('}');
    }

    private static void WriteList(StringBuilder builder, IList<object> list, int indent, int depth)
    {
        if (list.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append('[');
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(indent < 0 ? ", " : ",");
            }

            AppendNewLine(builder, indent, depth + 1);
            Write(builder, list[i], indent, depth + 1);
        }

        AppendNewLine(builder, indent, depth);
        builder.Append(']');
    }

    private static void AppendNewLine(StringBuilder builder, int indent, int depth)
    {
        if (indent < 0)
        {
            return;
        }

        builder.Append('\n');
        builder.Append(' ', indent * depth);
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (char character in text)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
