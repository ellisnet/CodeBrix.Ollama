using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Reads the Jinja fixtures copied beside the test assembly: the template itself, the JSON variables
/// for one case, and the expected rendering. The JSON is turned into the engine's value model - an
/// insertion-ordered mapping, a list, a string, a bool, a long, a double or null - so that
/// <c>| tojson</c> reproduces the key order of the file.
/// </summary>
internal static class JinjaFixtureLoader
{
    /// <summary>The folder the fixtures are copied to.</summary>
    internal static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Jinja");

    /// <summary>Reads one template.</summary>
    /// <param name="templateName">The short template name, without the extension.</param>
    /// <returns>The parsed template.</returns>
    internal static JinjaTemplate LoadTemplate(string templateName)
    {
        return JinjaTemplate.Parse(File.ReadAllText(Path.Combine(Directory, templateName + ".jinja")));
    }

    /// <summary>Reads the variables for one case.</summary>
    /// <param name="templateName">The short template name.</param>
    /// <param name="caseName">The case name.</param>
    /// <returns>The variables.</returns>
    internal static IReadOnlyDictionary<string, object> LoadVariables(string templateName, string caseName)
    {
        string path = Path.Combine(Directory, templateName + "." + caseName + ".input.json");
        var variables = new Dictionary<string, object>(StringComparer.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                variables[property.Name] = Convert(property.Value);
            }
        }

        return variables;
    }

    /// <summary>Reads the expected rendering for one case.</summary>
    /// <param name="templateName">The short template name.</param>
    /// <param name="caseName">The case name.</param>
    /// <returns>The expected text, byte for byte.</returns>
    internal static string LoadExpected(string templateName, string caseName)
    {
        return File.ReadAllText(Path.Combine(Directory, templateName + "." + caseName + ".expected.txt"));
    }

    private static object Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var mapping = new OrderedDictionary<string, object>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    mapping[property.Name] = Convert(property.Value);
                }

                return mapping;
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(Convert(item));
                }

                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out long integer) ? integer : (object)element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}
