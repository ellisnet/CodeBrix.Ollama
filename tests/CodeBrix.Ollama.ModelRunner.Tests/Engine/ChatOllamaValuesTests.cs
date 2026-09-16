using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The values an Ollama Go chat template renders from, built from a request.
/// </summary>
public sealed class ChatOllamaValuesTests
{
    /// <summary>The conversation, the tools and the thinking switch are passed through unchanged.</summary>
    [Fact]
    public void Build_passes_the_conversation_the_tools_and_the_thinking_switch()
    {
        //Arrange
        ChatRequest request = new ChatRequest { Think = true };
        request.Messages.Add(new ChatMessage(ChatRole.System, "be brief"));
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));
        request.Tools.Add(new ToolDefinition { Name = "get_weather" });

        //Act
        OllamaTemplateValues values = ChatOllamaValues.Build(request);

        //Assert
        values.Messages.Should().HaveCount(2);
        values.Messages[0].Role.Should().Be(ChatRole.System);
        values.Messages[1].Content.Should().Be("hello");
        values.Tools.Should().HaveCount(1);
        values.Think.Should().Be(true);
        values.System.Should().BeNull();
        values.Prompt.Should().BeNull();
    }

    /// <summary>A request with no opinion about thinking leaves the template's own default standing.</summary>
    [Fact]
    public void Build_leaves_think_unset_when_the_request_did_not_set_it()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        OllamaTemplateValues values = ChatOllamaValues.Build(request);

        //Assert
        values.Think.HasValue.Should().BeFalse();
    }
}
