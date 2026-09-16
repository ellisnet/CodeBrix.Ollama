using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner; //was previously: nlohmann/json serializer dump() semantics (reference only);

/// <summary>
/// Writes a <see cref="JsonElement"/> back out as the most compact JSON text that still means the same
/// thing: no whitespace between tokens, and only the escapes JSON requires.
/// </summary>
/// <remarks>
/// The escaping deliberately matches <c>nlohmann::json::dump()</c>, which is what the llama.cpp schema
/// converter this library ports its grammar builder from uses to turn a constant into a grammar literal.
/// Non-ASCII characters are left as they are, control characters below <c>U+0020</c> use the short escape
/// where JSON has one and <c>\u00xx</c> with lower-case hex otherwise, and nothing else is escaped. The
/// framework's own writer differs on both counts, which is why this is written out by hand.
/// </remarks>
internal static class CompactJson
{
    /// <summary>
    /// Renders an element as compact JSON text.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The JSON text.</returns>
    public static string Dump(JsonElement element)
    {
        StringBuilder builder = new StringBuilder();
        Write(element, builder);
        return builder.ToString();
    }

    /// <summary>
    /// Renders a string value as a JSON string literal, quotes included.
    /// </summary>
    /// <param name="value">The string. <see langword="null"/> is rendered as the JSON null literal.</param>
    /// <returns>The JSON text.</returns>
    public static string DumpString(string value)
    {
        if (value == null)
        {
            return "null";
        }

        StringBuilder builder = new StringBuilder();
        WriteString(value, builder);
        return builder.ToString();
    }

    /// <summary>
    /// Writes one element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="builder">The buffer being built.</param>
    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                builder.Append('{');
                bool first = true;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(property.Name, builder);
                    builder.Append(':');
                    Write(property.Value, builder);
                }

                builder.Append('}');
                break;
            }

            case JsonValueKind.Array:
            {
                builder.Append('[');
                bool first = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    Write(item, builder);
                }

                builder.Append(']');
                break;
            }

            case JsonValueKind.String:
                WriteString(element.GetString(), builder);
                break;

            case JsonValueKind.True:
                builder.Append("true");
                break;

            case JsonValueKind.False:
                builder.Append("false");
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                builder.Append("null");
                break;

            default:
                //A number keeps the spelling the document used, which is what a round trip through
                //nlohmann's own number writer produces for every integer and for the decimals a schema or
                //a model realistically writes.
                builder.Append(element.GetRawText());
                break;
        }
    }

    /// <summary>
    /// Writes a JSON string literal, quotes included.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <param name="builder">The buffer being built.</param>
    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
