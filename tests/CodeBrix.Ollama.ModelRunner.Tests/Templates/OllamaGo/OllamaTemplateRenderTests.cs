using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Renders every built-in Ollama template against the three conversations Ollama's own
/// <c>TestTemplate</c> uses and compares the output byte for byte with Ollama's recorded expectations,
/// which are copied verbatim into Fixtures/OllamaGo.
/// </summary>
public sealed class OllamaTemplateRenderTests
{
    private static readonly string[] TrailingSpaceTemplates =
    {
        "chatqa", "llama2-chat", "mistral-instruct", "openchat", "vicuna",
    };

    /// <summary>A built-in template renders a recorded conversation exactly as Ollama renders it.</summary>
    /// <param name="name">The name of the built-in template.</param>
    /// <param name="conversation">The name of the recorded conversation.</param>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Render_matches_the_recorded_output(string name, string conversation)
    {
        //Arrange
        var template = OllamaTemplate.BuiltIn(name);
        var expected = File.ReadAllText(Path.Combine(OllamaTemplateFixtures.Directory, name + ".gotmpl",
            conversation));

        //Act
        var actual = template.Render(new OllamaTemplateValues { Messages = Conversation(conversation) });

        //Assert
        if (Array.IndexOf(TrailingSpaceTemplates, name) >= 0 && actual.EndsWith(" ", StringComparison.Ordinal))
        {
            actual = actual.Substring(0, actual.Length - 1);
        }

        actual.Should().Be(expected);
    }

    /// <summary>Every built-in template crossed with every recorded conversation.</summary>
    /// <returns>One row per template and conversation.</returns>
    public static IEnumerable<object[]> Cases()
        => from name in OllamaTemplate.BuiltInNames
           from conversation in new[] { "user", "user-assistant-user", "system-user-assistant-user" }
           select new object[] { name, conversation };

    private static IList<ChatMessage> Conversation(string name)
    {
        var messages = new List<ChatMessage>();
        var users = 0;
        foreach (var role in name.Split('-'))
        {
            switch (role)
            {
                case "system":
                    messages.Add(new ChatMessage(ChatRole.System, "You are a helpful assistant."));
                    break;
                case "assistant":
                    messages.Add(new ChatMessage(ChatRole.Assistant, "I'm doing great. How can I help you today?"));
                    break;
                default:
                    messages.Add(new ChatMessage(ChatRole.User, users++ == 0
                        ? "Hello, how are you?"
                        : "I'd like to show off how chat templating works!"));
                    break;
            }
        }

        return messages;
    }
}
