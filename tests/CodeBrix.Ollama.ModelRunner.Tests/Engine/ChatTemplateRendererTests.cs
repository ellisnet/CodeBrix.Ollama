using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Rendering a chat request in each dialect, without a model: the renderer needs only template text and the
/// two token strings, which is what makes the whole rendering path testable offline.
/// </summary>
public sealed class ChatTemplateRendererTests
{
    private const string ChatMlJinja =
        "{%- for message in messages %}{{ '<|im_start|>' + message['role'] + '\\n' + message['content'] "
        + "+ '<|im_end|>\\n' }}{%- endfor %}{%- if add_generation_prompt %}{{ '<|im_start|>assistant\\n' }}"
        + "{%- endif %}";

    private const string BosJinja = "{{ bos_token }}{% for m in messages %}{{ m['content'] }}{% endfor %}";

    private const string ToolCallsJinja =
        "{%- for message in messages %}[{{ message['role'] }}]"
        + "{%- if message.tool_calls %}{%- for call in message.tool_calls %}"
        + "<call>{{ call.function.name }}</call>{%- endfor %}{%- endif %}{%- endfor %}";

    private const string ToolsJinja =
        "{%- if tools %}{%- for tool in tools %}<tool>{{ tool.function.name }}</tool>{%- endfor %}{%- endif %}"
        + "{%- for message in messages %}[{{ message['role'] }}]{{ message['content'] }}{%- endfor %}";

    /// <summary>The Jinja dialect renders the conversation and asks for a reply.</summary>
    [Fact]
    public void Render_in_the_jinja_dialect_renders_the_conversation()
    {
        //Arrange
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, null, null, ChatMlJinja, "", "<|im_end|>");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.System, "be brief"));
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        string prompt = renderer.Render(request);

        //Assert
        prompt.Should().Be(
            "<|im_start|>system\nbe brief<|im_end|>\n<|im_start|>user\nhello<|im_end|>\n<|im_start|>assistant\n");
    }

    /// <summary>The template in the options replaces the one the model file embeds.</summary>
    [Fact]
    public void Render_in_the_jinja_dialect_prefers_the_template_from_the_options()
    {
        //Arrange
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, "OPTIONS:{{ messages[0]['content'] }}", null, ChatMlJinja, "", "");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        string prompt = renderer.Render(request);

        //Assert
        prompt.Should().Be("OPTIONS:hello");
    }

    /// <summary>The beginning-of-sequence token reaches a template that writes it.</summary>
    [Fact]
    public void Render_in_the_jinja_dialect_passes_the_beginning_of_sequence_token()
    {
        //Arrange
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, BosJinja, null, null, "<s>", "</s>");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        string prompt = renderer.Render(request);

        //Assert
        prompt.Should().Be("<s>hello");
    }

    /// <summary>The tools on offer reach a template that renders them.</summary>
    [Fact]
    public void Render_in_the_jinja_dialect_renders_the_tools()
    {
        //Arrange
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, ToolsJinja, null, null, "", "");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "weather?"));
        request.Tools.Add(new ToolDefinition { Name = "get_weather" });

        //Act
        string prompt = renderer.Render(request);

        //Assert
        prompt.Should().Be("<tool>get_weather</tool>[user]weather?");
    }

    /// <summary>A Jinja dialect with no template behind it says so rather than rendering nothing.</summary>
    [Fact]
    public void Render_in_the_jinja_dialect_with_no_template_is_refused()
    {
        //Arrange
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, null, null, null, "", "");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        Action act = () => renderer.Render(request);

        //Assert
        act.Should().Throw<ChatTemplateException>();
    }

    /// <summary>The Ollama dialect renders through the Go template engine.</summary>
    [Fact]
    public void Render_in_the_ollama_dialect_renders_the_conversation()
    {
        //Arrange
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Ollama, null, OllamaTemplate.BuiltIn("chatml").Source, ChatMlJinja, "", "");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.System, "be brief"));
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        string prompt = renderer.Render(request);

        //Assert
        prompt.Should().Be(
            "<|im_start|>system\nbe brief<|im_end|>\n<|im_start|>user\nhello<|im_end|>\n<|im_start|>assistant\n");
    }

    /// <summary>A message whose tool-call list is null renders as a message with no tool calls.</summary>
    [Fact]
    public void Render_accepts_a_message_whose_tool_calls_are_null()
    {
        //Arrange
        ChatTemplateRenderer ollama = new ChatTemplateRenderer(
            ChatTemplateDialect.Ollama, null, OllamaTemplate.BuiltIn("command-r").Source, null, "", "");

        ChatTemplateRenderer jinja = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, null, null, ChatMlJinja, "", "<|im_end|>");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));
        request.Messages.Add(new ChatMessage(ChatRole.Assistant, "hi there") { ToolCalls = null });

        //Act
        string byOllama = ollama.Render(request);
        string byJinja = jinja.Render(request);

        //Assert
        byOllama.Should().Contain("hi there");
        byJinja.Should().Contain("hi there");
    }

    /// <summary>A null inside a message's tool-call list is not a call, and does not reach the template.</summary>
    [Fact]
    public void Render_accepts_a_null_inside_a_messages_tool_call_list()
    {
        //Arrange
        ChatTemplateRenderer ollama = new ChatTemplateRenderer(
            ChatTemplateDialect.Ollama, null, OllamaTemplate.BuiltIn("command-r").Source, null, "", "");

        ChatTemplateRenderer jinja = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, null, null, ToolCallsJinja, "", "");

        // The content is empty on purpose: command-r renders the calls only when there is no answer text.
        ChatMessage assistant = new ChatMessage(ChatRole.Assistant, string.Empty);
        assistant.ToolCalls.Add(null);
        assistant.ToolCalls.Add(new ToolCall { Id = "call_1", Name = "get_weather", ArgumentsJson = "{}" });

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));
        request.Messages.Add(assistant);

        //Act
        string byOllama = ollama.Render(request);
        string byJinja = jinja.Render(request);

        //Assert
        byOllama.Should().Contain("get_weather");
        byJinja.Should().Contain("get_weather");
    }

    /// <summary>An Ollama template that is one of Ollama's own brings its stop strings with it.</summary>
    [Fact]
    public void StopStrings_are_taken_from_a_built_in_ollama_template()
    {
        //Arrange and act
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Ollama, null, OllamaTemplate.BuiltIn("chatml").Source, null, "", "");

        //Assert
        renderer.StopStrings.Should().Contain("<|im_end|>");
        renderer.StopStrings.Should().Contain("<|im_start|>");
    }

    /// <summary>A template of the caller's own brings none, because nothing is known about it.</summary>
    [Fact]
    public void StopStrings_are_empty_for_a_template_of_the_callers_own()
    {
        //Arrange and act
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Ollama, null, "{{ .Prompt }}", null, "", "");

        //Assert
        renderer.StopStrings.Should().BeEmpty();
    }

    /// <summary>A Jinja dialect never carries stop strings, whatever template it was given.</summary>
    [Fact]
    public void StopStrings_are_empty_for_the_jinja_dialect()
    {
        //Arrange and act
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Jinja, null, OllamaTemplate.BuiltIn("chatml").Source, ChatMlJinja, "", "");

        //Assert
        renderer.StopStrings.Should().BeEmpty();
    }

    /// <summary>The native dialect cannot express a tool, and says so rather than dropping it.</summary>
    [Fact]
    public void Render_in_the_native_dialect_refuses_a_request_with_tools()
    {
        //Arrange
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            ChatTemplateDialect.Native, null, null, ChatMlJinja, "", "");

        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "weather?"));
        request.Tools.Add(new ToolDefinition { Name = "get_weather" });

        //Act
        Action act = () => renderer.Render(request);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("tools");
    }

    /// <summary>Each dialect reports the template text its parsers should be built from.</summary>
    [Theory]
    [InlineData(ChatTemplateDialect.Jinja, "jinja")]
    [InlineData(ChatTemplateDialect.Ollama, "ollama")]
    [InlineData(ChatTemplateDialect.Native, "embedded")]
    public void TemplateText_is_the_one_the_dialect_renders_with(ChatTemplateDialect dialect, string expected)
    {
        //Arrange and act
        ChatTemplateRenderer renderer = new ChatTemplateRenderer(
            dialect, "jinja", "ollama", "embedded", "", "");

        //Assert
        renderer.TemplateText.Should().Be(expected);
    }
}
