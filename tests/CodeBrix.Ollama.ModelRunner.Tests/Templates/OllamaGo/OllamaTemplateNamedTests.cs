using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Tests for matching an arbitrary chat template - the Jinja <c>chat_template</c> a model file carries -
/// to one of Ollama's built-ins, against the template corpus Ollama tests its own matcher with.
/// </summary>
public sealed class OllamaTemplateNamedTests
{
    /// <summary>A recorded chat template matches the built-in Ollama matches it to, and that built-in parses.</summary>
    /// <param name="expected">The name of the built-in Ollama picks.</param>
    /// <param name="template">The chat template text to match.</param>
    [Theory]
    [MemberData(nameof(Cases))]
    public void NamedBuiltIn_picks_the_template_Ollama_picks(string expected, string template)
    {
        //Act
        var name = OllamaTemplate.NamedBuiltIn(template);

        //Assert
        name.Should().Be(expected);
        OllamaTemplate.BuiltIn(name).GoTemplate.Root.ToString().Should().NotBeEmpty();
    }

    /// <summary>Text that looks nothing like a chat template matches no built-in.</summary>
    [Fact]
    public void NamedBuiltIn_with_unrelated_text_returns_null()
    {
        //Act
        var name = OllamaTemplate.NamedBuiltIn(new string('z', 4000));

        //Assert
        name.Should().BeNull();
    }

    /// <summary>The recorded chat templates and the built-in each one should match.</summary>
    /// <returns>One row per recorded template.</returns>
    public static IEnumerable<object[]> Cases()
    {
        var path = Path.Combine(OllamaTemplateFixtures.Directory, "templates.jsonl");
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                yield return new object[] { property.Name, property.Value.GetString() };
            }
        }
    }
}
