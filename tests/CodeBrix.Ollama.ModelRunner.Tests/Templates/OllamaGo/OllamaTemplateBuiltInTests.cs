using System.Collections.Generic;
using System.Linq;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Tests for the twenty built-in Ollama chat templates that ship inside the assembly, their index and
/// their stop-parameter sidecars.
/// </summary>
public sealed class OllamaTemplateBuiltInTests
{
    /// <summary>All forty-one embedded built-in resources are present and readable.</summary>
    [Fact]
    public void All_forty_one_built_in_resources_load()
    {
        //Act
        var files = OllamaTemplateBuiltIns.ResourceFileNames();

        //Assert
        files.Should().HaveCount(41);
        files.Count(f => f.EndsWith(".gotmpl")).Should().Be(20);
        files.Count(f => f.EndsWith(".json")).Should().Be(21);
        files.Should().Contain("index.json");
    }

    /// <summary>Every built-in name resolves to template text and to a parameter sidecar.</summary>
    [Fact]
    public void BuiltInNames_each_resolve_to_text_and_parameters()
    {
        //Arrange
        var names = OllamaTemplate.BuiltInNames;

        //Assert
        names.Should().HaveCount(20);
        foreach (var name in names)
        {
            OllamaTemplateBuiltIns.ReadTemplate(name).Should().NotBeNull();
            OllamaTemplate.BuiltInStopStrings(name).Should().NotBeNull();
        }
    }

    /// <summary>The index Ollama matches unknown templates against loads and names only real built-ins.</summary>
    [Fact]
    public void Index_entries_all_name_a_built_in()
    {
        //Arrange
        var names = new HashSet<string>(OllamaTemplate.BuiltInNames);

        //Act
        var index = OllamaTemplateBuiltIns.Index;

        //Assert
        index.Should().HaveCount(37);
        foreach (var entry in index)
        {
            names.Contains(entry.Name).Should().BeTrue();
            entry.Template.Should().NotBeNull();
        }
    }

    /// <summary>Every built-in parses and renders a two-message conversation without throwing.</summary>
    /// <param name="name">The name of the built-in template.</param>
    [Theory]
    [MemberData(nameof(BuiltInNames))]
    public void BuiltIn_parses_and_renders_a_conversation(string name)
    {
        //Arrange
        var template = OllamaTemplate.BuiltIn(name);
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Hello, how are you?"),
                new ChatMessage(ChatRole.Assistant, "I'm doing great. How can I help you today?"),
            },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        template.Source.Should().NotBeNull();
        rendered.Should().Contain("Hello, how are you?");
    }

    /// <summary>The names of the built-in templates, as theory data.</summary>
    /// <returns>One row per built-in name.</returns>
    public static IEnumerable<object[]> BuiltInNames()
        => OllamaTemplate.BuiltInNames.Select(name => new object[] { name });
}
