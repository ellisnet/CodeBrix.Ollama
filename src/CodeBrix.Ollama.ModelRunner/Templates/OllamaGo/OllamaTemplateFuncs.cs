using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama template/template.go and api/types.go;

/// <summary>
/// The functions Ollama adds to Go's built-in template functions before it parses a chat template.
/// </summary>
internal static class OllamaTemplateFuncs
{
    private static readonly Dictionary<string, GoFunction> Table = Build();

    [ThreadStatic]
    private static DateTime? _todayOverride;

    /// <summary>Ollama's function table, keyed by name.</summary>
    internal static IReadOnlyDictionary<string, GoFunction> Funcs => Table;

    /// <summary>
    /// The day the date functions report. Tests set it so that a rendering is reproducible; it is
    /// <see langword="null"/> in normal use, which means today. It is thread-local, so one test's
    /// setting cannot reach a test running beside it.
    /// </summary>
    internal static DateTime? TodayOverride
    {
        get => _todayOverride;
        set => _todayOverride = value;
    }

    private static Dictionary<string, GoFunction> Build()
    {
        Dictionary<string, GoFunction> table = new Dictionary<string, GoFunction>(StringComparer.Ordinal)
        {
            ["json"] = new GoFunction("json", 1, 1, args => GoJson.Marshal(args[0])),
            ["currentDate"] = new GoFunction("currentDate", 0, -1, args => DateArgument(args,
                Today().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            ["yesterdayDate"] = new GoFunction("yesterdayDate", 0, -1, args => DateArgument(args,
                Today().AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            ["toTypeScriptType"] = new GoFunction("toTypeScriptType", 1, 1, args => ToTypeScriptType(args[0])),
        };
        return table;
    }

    /// <summary>
    /// Applies the argument rule of Ollama's <c>func(args ...string) string</c> date functions: the
    /// arguments are ignored, but each one has to be a string or Go's call would not type-check.
    /// </summary>
    /// <param name="args">The arguments the template passed.</param>
    /// <param name="formatted">The date the function reports.</param>
    /// <returns>The date.</returns>
    private static object DateArgument(IReadOnlyList<object> args, string formatted)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (!(args[i] is string))
            {
                throw new ChatTemplateException(string.Format(CultureInfo.InvariantCulture,
                    "wrong type for value; expected string; got {0}", GoValue.TypeName(args[i])));
            }
        }

        return formatted;
    }

    private static DateTime Today() => _todayOverride ?? DateTime.Now;

    private static string ToTypeScriptType(object value)
    {
        if (!(value is GoStruct property) || property.TypeName != OllamaTemplateBinder.ToolPropertyTypeName)
        {
            return "any";
        }

        if (property.TryGetField("AnyOf", out GoStructField anyOf) && anyOf.Value is GoSlice options
            && options.Items.Count > 0)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < options.Items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(ToTypeScriptType(options.Items[i]));
            }

            return builder.ToString();
        }

        if (!property.TryGetField("Type", out GoStructField type) || !(type.Value is GoSlice types)
            || types.Items.Count == 0)
        {
            return "any";
        }

        StringBuilder result = new StringBuilder();
        for (int i = 0; i < types.Items.Count; i++)
        {
            if (i > 0)
            {
                result.Append(" | ");
            }

            result.Append(MapToTypeScriptType(types.Items[i] as string));
        }

        return result.ToString();
    }

    private static string MapToTypeScriptType(string jsonType)
    {
        switch (jsonType)
        {
            case "string":
                return "string";
            case "number":
            case "integer":
                return "number";
            case "boolean":
                return "boolean";
            case "array":
                return "any[]";
            case "object":
                return "Record<string, any>";
            case "null":
                return "null";
            default:
                return "any";
        }
    }
}
