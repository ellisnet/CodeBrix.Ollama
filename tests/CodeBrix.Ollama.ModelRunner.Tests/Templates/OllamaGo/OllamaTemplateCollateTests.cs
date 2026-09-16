using System.Collections.Generic;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Tests for the message collation Ollama runs before a template sees a conversation: consecutive
/// messages of one role are merged, tool messages are left alone, and the system messages are collected.
/// </summary>
public sealed class OllamaTemplateCollateTests
{
    /// <summary>Consecutive messages from the same role are merged with a blank line between them.</summary>
    [Fact]
    public void Collate_merges_consecutive_user_messages()
    {
        //Arrange
        var values = Values(
            new ChatMessage(ChatRole.User, "Hello"),
            new ChatMessage(ChatRole.User, "How are you?"));

        //Act
        OllamaTemplate.Collate(values, out var system, out var collated);

        //Assert
        system.Should().BeEmpty();
        collated.Should().HaveCount(1);
        collated[0].Content.Should().Be("Hello\n\nHow are you?");
    }

    /// <summary>Consecutive tool results keep their own identities rather than being merged.</summary>
    [Fact]
    public void Collate_keeps_consecutive_tool_messages_apart()
    {
        //Arrange
        var values = Values(
            new ChatMessage { Role = ChatRole.Tool, Content = "sunny", ToolName = "get_weather" },
            new ChatMessage { Role = ChatRole.Tool, Content = "72F", ToolName = "get_temperature" });

        //Act
        OllamaTemplate.Collate(values, out var system, out var collated);

        //Assert
        system.Should().BeEmpty();
        collated.Should().HaveCount(2);
        collated[0].ToolName.Should().Be("get_weather");
        collated[1].ToolName.Should().Be("get_temperature");
    }

    /// <summary>A tool result keeps every field it arrived with.</summary>
    [Fact]
    public void Collate_preserves_tool_message_fields()
    {
        //Arrange
        var values = Values(
            new ChatMessage(ChatRole.User, "What's the weather?"),
            new ChatMessage { Role = ChatRole.Tool, Content = "sunny", ToolName = "get_conditions" },
            new ChatMessage { Role = ChatRole.Tool, Content = "72F", ToolName = "get_temperature" });

        //Act
        OllamaTemplate.Collate(values, out _, out var collated);

        //Assert
        collated.Should().HaveCount(3);
        collated[1].Content.Should().Be("sunny");
        collated[2].Content.Should().Be("72F");
    }

    /// <summary>A mixed conversation keeps its order and reports the system message separately.</summary>
    [Fact]
    public void Collate_with_a_mixed_conversation()
    {
        //Arrange
        var values = Values(
            new ChatMessage(ChatRole.System, "You are helpful"),
            new ChatMessage(ChatRole.User, "Hello"),
            new ChatMessage(ChatRole.Assistant, "Hi there!"),
            new ChatMessage(ChatRole.User, "What's the weather?"),
            new ChatMessage { Role = ChatRole.Tool, Content = "sunny", ToolName = "get_weather" },
            new ChatMessage { Role = ChatRole.Tool, Content = "72F", ToolName = "get_temperature" },
            new ChatMessage(ChatRole.User, "Thanks"));

        //Act
        OllamaTemplate.Collate(values, out var system, out var collated);

        //Assert
        system.Should().Be("You are helpful");
        collated.Should().HaveCount(7);
    }

    /// <summary>Two system messages are joined by a blank line.</summary>
    [Fact]
    public void Collate_joins_system_messages()
    {
        //Arrange
        var values = Values(
            new ChatMessage(ChatRole.System, "One"),
            new ChatMessage(ChatRole.User, "Hi"),
            new ChatMessage(ChatRole.System, "Two"));

        //Act
        OllamaTemplate.Collate(values, out var system, out _);

        //Assert
        system.Should().Be("One\n\nTwo");
    }

    private static OllamaTemplateValues Values(params ChatMessage[] messages)
        => new OllamaTemplateValues { Messages = new List<ChatMessage>(messages) };
}
