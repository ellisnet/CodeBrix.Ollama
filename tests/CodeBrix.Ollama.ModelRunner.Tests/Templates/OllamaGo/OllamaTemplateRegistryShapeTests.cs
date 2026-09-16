using System.Collections.Generic;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Renders a template shaped like the tool-calling templates the Ollama registry ships - the Llama 3.1
/// family - which leans on the parts of the language the built-ins never reach: sub-pipelines,
/// <c>slice</c> over the message list, a variable declared per turn, and printing a tool as JSON.
/// </summary>
public sealed class OllamaTemplateRegistryShapeTests
{
    private const string Llama31 = "{{- if or .System .Tools }}<|start_header_id|>system<|end_header_id|>\n"
        + "{{- if .System }}\n\n{{ .System }}\n{{- end }}\n"
        + "{{- if .Tools }}\n\nYou have tools.\n{{- end }}<|eot_id|>\n"
        + "{{- end }}\n"
        + "{{- range $i, $_ := .Messages }}\n"
        + "{{- $last := eq (len (slice $.Messages $i)) 1 }}\n"
        + "{{- if eq .Role \"user\" }}<|start_header_id|>user<|end_header_id|>\n"
        + "{{- if and $.Tools $last }}\n\nHere are the tools:\n\n"
        + "{{ range $.Tools }}\n{{- . }}\n{{ end }}\n{{ .Content }}<|eot_id|>\n"
        + "{{- else }}\n\n{{ .Content }}<|eot_id|>\n"
        + "{{- end }}{{ if $last }}<|start_header_id|>assistant<|end_header_id|>\n\n{{ end }}\n"
        + "{{- else if eq .Role \"assistant\" }}<|start_header_id|>assistant<|end_header_id|>\n"
        + "{{- if .ToolCalls }}\n{{ range .ToolCalls }}\n"
        + "{\"name\": \"{{ .Function.Name }}\", \"parameters\": {{ .Function.Arguments }}}{{ end }}\n"
        + "{{- else }}\n\n{{ .Content }}\n"
        + "{{- end }}{{ if not $last }}<|eot_id|>{{ end }}\n"
        + "{{- else if eq .Role \"tool\" }}<|start_header_id|>ipython<|end_header_id|>\n\n{{ .Content }}<|eot_id|>"
        + "{{ if $last }}<|start_header_id|>assistant<|end_header_id|>\n\n{{ end }}\n"
        + "{{- end }}\n"
        + "{{- end }}";

    /// <summary>The template reports the identifiers a tool-calling template uses.</summary>
    [Fact]
    public void Variables_of_a_tool_calling_template()
    {
        //Act
        var template = OllamaTemplate.Parse(Llama31);

        //Assert
        template.Variables.Should().Contain("messages");
        template.Variables.Should().Contain("tools");
        template.Variables.Should().Contain("toolcalls");
    }

    /// <summary>A plain exchange renders the header sequence the model expects.</summary>
    [Fact]
    public void Render_a_plain_exchange()
    {
        //Arrange
        var template = OllamaTemplate.Parse(Llama31);
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "Be brief."),
                new ChatMessage(ChatRole.User, "Hi"),
            },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Be("<|start_header_id|>system<|end_header_id|>\n\nBe brief.<|eot_id|>"
            + "<|start_header_id|>user<|end_header_id|>\n\nHi<|eot_id|>"
            + "<|start_header_id|>assistant<|end_header_id|>\n\n");
    }

    /// <summary>The tools are printed as JSON beside the last user turn.</summary>
    [Fact]
    public void Render_with_tools_prints_them_as_json()
    {
        //Arrange
        var template = OllamaTemplate.Parse(Llama31);
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "Weather?") },
            Tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = "get_weather",
                    Description = "Get the weather",
                    ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"city\"],\"properties\":"
                        + "{\"city\":{\"type\":\"string\",\"description\":\"The city\"}}}",
                },
            },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Contain("You have tools.");
        rendered.Should().Contain("{\"type\":\"function\",\"function\":{\"name\":\"get_weather\","
            + "\"description\":\"Get the weather\",\"parameters\":{\"type\":\"object\",\"required\":[\"city\"],"
            + "\"properties\":{\"city\":{\"type\":\"string\",\"description\":\"The city\"}}}}}");
        rendered.Should().Contain("Weather?<|eot_id|><|start_header_id|>assistant<|end_header_id|>");
    }

    /// <summary>A tool call and its result render through the assistant and ipython turns.</summary>
    [Fact]
    public void Render_a_tool_call_and_its_result()
    {
        //Arrange
        var template = OllamaTemplate.Parse(Llama31);
        var values = new OllamaTemplateValues
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "Weather?"),
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = string.Empty,
                    ToolCalls = new List<ToolCall>
                    {
                        new ToolCall { Name = "get_weather", ArgumentsJson = "{\"city\":\"Tokyo\"}" },
                    },
                },
                new ChatMessage { Role = ChatRole.Tool, Content = "sunny", ToolName = "get_weather" },
            },
        };

        //Act
        var rendered = template.Render(values);

        //Assert
        rendered.Should().Contain("{\"name\": \"get_weather\", \"parameters\": {\"city\":\"Tokyo\"}}");
        rendered.Should().Contain("<|start_header_id|>ipython<|end_header_id|>\n\nsunny<|eot_id|>");
        rendered.Should().EndWith("<|start_header_id|>assistant<|end_header_id|>\n\n");
    }
}
