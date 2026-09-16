using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Tests for rendering a conversation, covering both template forms, the fill-in-the-middle form, the
/// date functions and the tool shapes a template sees.
/// </summary>
public sealed class OllamaTemplateExecuteTests
{
    private const string MistralNoResponse = "[INST] {{ if .System }}{{ .System }}\n\n{{ end }}{{ .Prompt }}[/INST] ";
    private const string MistralResponse =
        "[INST] {{ if .System }}{{ .System }}\n\n{{ end }}{{ .Prompt }}[/INST] {{ .Response }}";
    private const string MistralMessages = "[INST] {{ if .System }}{{ .System }}\n\n{{ end }}\n"
        + "{{- range .Messages }}\n"
        + "{{- if eq .Role \"user\" }}{{ .Content }}[/INST] {{ else if eq .Role \"assistant\" }}{{ .Content }}[INST] {{ end }}\n"
        + "{{- end }}";

    /// <summary>All three shapes of the Mistral template render a conversation identically.</summary>
    /// <param name="template">The template text.</param>
    [Theory]
    [InlineData(MistralNoResponse)]
    [InlineData(MistralResponse)]
    [InlineData(MistralMessages)]
    public void Render_mistral_renders_the_same_prompt_from_every_form(string template)
    {
        //Arrange
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Hello friend!"),
                new ChatMessage(ChatRole.Assistant, "Hello human!"),
                new ChatMessage(ChatRole.User, "What is your name?"),
            },
        };

        //Act
        var rendered = OllamaTemplate.Parse(template).Render(values);

        //Assert
        rendered.Should().Be("[INST] Hello friend![/INST] Hello human![INST] What is your name?[/INST] ");
    }

    /// <summary>A leading system message reaches every shape of the Mistral template.</summary>
    /// <param name="template">The template text.</param>
    [Theory]
    [InlineData(MistralNoResponse)]
    [InlineData(MistralResponse)]
    [InlineData(MistralMessages)]
    public void Render_mistral_with_a_system_message(string template)
    {
        //Arrange
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "You are a helpful assistant!"),
                new ChatMessage(ChatRole.User, "Hello friend!"),
                new ChatMessage(ChatRole.Assistant, "Hello human!"),
                new ChatMessage(ChatRole.User, "What is your name?"),
            },
        };

        //Act
        var rendered = OllamaTemplate.Parse(template).Render(values);

        //Assert
        rendered.Should().Be("[INST] You are a helpful assistant!\n\nHello friend![/INST] Hello human!"
            + "[INST] What is your name?[/INST] ");
    }

    /// <summary>A conversation that ends mid-answer leaves the answer open for the model to continue.</summary>
    /// <param name="template">The template text.</param>
    [Theory]
    [InlineData("[INST] {{ .Prompt }}[/INST] ")]
    [InlineData("[INST] {{ .Prompt }}[/INST] {{ .Response }}")]
    [InlineData("\n{{- range $i, $m := .Messages }}\n"
        + "{{- if eq .Role \"user\" }}[INST] {{ .Content }}[/INST] {{ else if eq .Role \"assistant\" }}{{ .Content }}{{ end }}\n"
        + "{{- end }}")]
    public void Render_mistral_with_a_trailing_assistant_message(string template)
    {
        //Arrange
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Hello friend!"),
                new ChatMessage(ChatRole.Assistant, "Hello human!"),
                new ChatMessage(ChatRole.User, "What is your name?"),
                new ChatMessage(ChatRole.Assistant, "My name is Ollama and I"),
            },
        };

        //Act
        var rendered = OllamaTemplate.Parse(template).Render(values);

        //Assert
        rendered.Should().Be("[INST] Hello friend![/INST] Hello human![INST] What is your name?[/INST] "
            + "My name is Ollama and I");
    }

    /// <summary>Both shapes of a ChatML template render the same conversation.</summary>
    /// <param name="template">The template text.</param>
    [Theory]
    [InlineData("{{ if .System }}<|im_start|>system\n{{ .System }}<|im_end|>\n{{ end }}"
        + "{{ if .Prompt }}<|im_start|>user\n{{ .Prompt }}<|im_end|>\n{{ end }}<|im_start|>assistant\n"
        + "{{ .Response }}<|im_end|>\n")]
    [InlineData("\n{{- range $index, $_ := .Messages }}<|im_start|>{{ .Role }}\n{{ .Content }}<|im_end|>\n"
        + "{{ end }}<|im_start|>assistant\n")]
    public void Render_chatml_renders_the_same_prompt_from_every_form(string template)
    {
        //Arrange
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "You are a helpful assistant!"),
                new ChatMessage(ChatRole.User, "Hello friend!"),
                new ChatMessage(ChatRole.Assistant, "Hello human!"),
                new ChatMessage(ChatRole.User, "What is your name?"),
            },
        };

        //Act
        var rendered = OllamaTemplate.Parse(template).Render(values);

        //Assert
        rendered.Should().Be("<|im_start|>system\nYou are a helpful assistant!<|im_end|>\n"
            + "<|im_start|>user\nHello friend!<|im_end|>\n"
            + "<|im_start|>assistant\nHello human!<|im_end|>\n"
            + "<|im_start|>user\nWhat is your name?<|im_end|>\n"
            + "<|im_start|>assistant\n");
    }

    /// <summary>A message-only request renders the prompt branch of a fill-in-the-middle template.</summary>
    [Fact]
    public void Render_with_no_suffix_takes_the_prompt_branch()
    {
        //Arrange
        var template = OllamaTemplate.Parse(SuffixTemplate);
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "hello") },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("hello");
    }

    /// <summary>A prompt and a suffix together render the fill-in-the-middle branch.</summary>
    [Fact]
    public void Render_with_a_suffix_takes_the_suffix_branch()
    {
        //Arrange
        var template = OllamaTemplate.Parse(SuffixTemplate);
        var values = new OllamaTemplateValues { Prompt = "def add(", Suffix = "return x" };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("<PRE> def add( <SUF>return x <MID>");
    }

    /// <summary>The current date reaches a template that asks for it.</summary>
    [Fact]
    public void Render_with_currentDate_writes_todays_date()
    {
        //Arrange
        var template = OllamaTemplate.Parse("{{- range .Messages }}{{ .Content }}{{ end }} Today is {{ currentDate }}");
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "Hello") },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("Hello Today is " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>Yesterday's date reaches a template that asks for it, in the same format.</summary>
    [Fact]
    public void Render_with_yesterdayDate_writes_yesterdays_date()
    {
        //Arrange
        var template = OllamaTemplate.Parse(
            "{{- range .Messages }}{{ .Content }}{{ end }} Yesterday was {{ yesterdayDate }}");
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "Hello") },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("Hello Yesterday was "
            + DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>Tool call arguments render as JSON rather than as a Go map.</summary>
    [Fact]
    public void Render_tool_call_arguments_are_json()
    {
        //Arrange
        var template = OllamaTemplate.Parse(
            "{{- range .Messages }}{{- range .ToolCalls }}{{ .Function.Arguments }}{{- end }}{{- end }}");
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    ToolCalls = new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Name = "get_weather",
                            ArgumentsJson = "{\"location\":\"Tokyo\",\"unit\":\"celsius\"}",
                        },
                    },
                },
            },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().StartWith("{");
        rendered.Should().Be("{\"location\":\"Tokyo\",\"unit\":\"celsius\"}");
        JsonDocument.Parse(rendered).RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    /// <summary>A template can range over tool call arguments key by key.</summary>
    [Fact]
    public void Render_can_range_over_tool_call_arguments()
    {
        //Arrange
        var template = OllamaTemplate.Parse("{{- range .Messages }}{{- range .ToolCalls }}"
            + "{{- range $k, $v := .Function.Arguments }}{{ $k }}={{ $v }};{{- end }}{{- end }}{{- end }}");
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    ToolCalls = new List<ToolCall>
                    {
                        new ToolCall { Name = "get_weather", ArgumentsJson = "{\"city\":\"Tokyo\"}" },
                    },
                },
            },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("city=Tokyo;");
    }

    /// <summary>Tool parameter properties render as JSON rather than as a Go map.</summary>
    [Fact]
    public void Render_tool_properties_are_json()
    {
        //Arrange
        var template = OllamaTemplate.Parse(
            "{{- range .Messages }}{{- end }}{{- range .Tools }}{{ .Function.Parameters.Properties }}{{- end }}");

        //Act
        var rendered = template.Render(ToolValues());

        //Assert
        rendered.Should().StartWith("{");
        JsonDocument.Parse(rendered).RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    /// <summary>A template can range over tool parameter properties name by name.</summary>
    [Fact]
    public void Render_can_range_over_tool_properties()
    {
        //Arrange
        var template = OllamaTemplate.Parse("{{- range .Messages }}{{- end }}{{- range .Tools }}"
            + "{{- range $name, $prop := .Function.Parameters.Properties }}{{ $name }}:{{ $prop.Type }};"
            + "{{- end }}{{- end }}");

        //Act
        var rendered = template.Render(ToolValues());

        //Assert
        rendered.Should().Be("location:string;");
    }

    /// <summary>Printing the whole tool list yields the JSON Ollama prints.</summary>
    [Fact]
    public void Render_tools_prints_the_ollama_json()
    {
        //Arrange
        var template = OllamaTemplate.Parse("{{- range .Messages }}{{- end }}{{ .Tools }}");

        //Act
        var rendered = template.Render(ToolValues());

        //Assert
        rendered.Should().Be("[{\"type\":\"function\",\"function\":{\"name\":\"get_weather\","
            + "\"description\":\"Get weather\",\"parameters\":{\"type\":\"object\",\"properties\":"
            + "{\"location\":{\"type\":\"string\",\"description\":\"City name\"}}}}}]");
    }

    /// <summary>The thinking flags reach a template, and an unset flag is visible as unset.</summary>
    /// <param name="think">The value to render with.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData(null, "false|false|")]
    [InlineData(true, "true|true|high")]
    [InlineData(false, "false|true|high")]
    public void Render_passes_the_thinking_flags(bool? think, string expected)
    {
        //Arrange
        var template = OllamaTemplate.Parse(
            "{{- range .Messages }}{{- end }}{{ .Think }}|{{ .IsThinkSet }}|{{ .ThinkLevel }}");
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "hi") },
            Think = think,
            ThinkLevel = think.HasValue ? "high" : null,
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>A system prompt supplied outside the messages reaches the template as a system message.</summary>
    [Fact]
    public void Render_with_a_system_value_prepends_a_system_message()
    {
        //Arrange
        var template = OllamaTemplate.Parse("{{ .System }}|{{ range .Messages }}{{ .Role }}:{{ .Content }};{{ end }}");
        var values = new OllamaTemplateValues
        {
            System = "Be brief.",
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "hi") },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("Be brief.|system:Be brief.;user:hi;");
    }

    /// <summary>Rendering does not disturb the messages the caller handed in.</summary>
    [Fact]
    public void Render_does_not_mutate_the_caller_messages()
    {
        //Arrange
        var first = new ChatMessage(ChatRole.User, "Hello");
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage> { first, new ChatMessage(ChatRole.User, "How are you?") },
        };

        //Act
        OllamaTemplate.Parse("{{ range .Messages }}{{ .Content }}{{ end }}").Render(values);

        //Assert
        first.Content.Should().Be("Hello");
    }

    /// <summary>The older rendering can be forced for a template that ranges over the messages.</summary>
    [Fact]
    public void Render_with_forced_legacy_uses_the_prompt_and_response_form()
    {
        //Arrange
        var template = OllamaTemplate.Parse("<s>{{ .System }}|{{ .Prompt }}|{{ .Response }}</s>"
            + "{{ range .Messages }}{{ end }}");
        var values = new OllamaTemplateValues
        {
            ForceLegacy = true,
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "one"),
                new ChatMessage(ChatRole.Assistant, "two"),
                new ChatMessage(ChatRole.User, "three"),
            },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("<s>|one|two</s><s>|three|");
    }

    private const string SuffixTemplate = "{{- if .Suffix }}<PRE> {{ .Prompt }} <SUF>{{ .Suffix }} <MID>\n"
        + "{{- else }}{{ .Prompt }}\n{{- end }}";

    private static OllamaTemplateValues ToolValues()
        => new OllamaTemplateValues
        {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "test") },
            Tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = "get_weather",
                    Description = "Get weather",
                    ParametersJsonSchema = "{\"type\":\"object\",\"properties\":"
                        + "{\"location\":{\"type\":\"string\",\"description\":\"City name\"}}}",
                },
            },
        };
}
