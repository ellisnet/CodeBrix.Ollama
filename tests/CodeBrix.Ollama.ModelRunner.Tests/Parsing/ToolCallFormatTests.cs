using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the tool-call prefix inference against every case in Ollama's own template tests, and the Jinja
/// and rendered-output routes this library adds.
/// </summary>
public sealed class ToolCallFormatTests
{
    /// <summary>The prefix is read off the tool-call branch of a Go chat template.</summary>
    [Theory]
    [InlineData("", "{")]
    [InlineData("{{if .ToolCalls}}{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}{{ . }}{{end}}{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}```json\n{{end}}", "```json")]
    [InlineData("{{if .ToolCalls}}[{{range .ToolCalls}}{{ . }}{{end}}]{{end}}", "[")]
    [InlineData("{{if .ToolCalls}}\n [ {{range .ToolCalls}}{{ . }}{{end}}]{{end}}", "[")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}{{ . }}{{end}}]{{end}}", "{")]
    [InlineData("{{if .ToolCalls}} {{range .ToolCalls}}{{ . }}{{end}}{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}\n{{ . }}\n{{end}}{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}{\"name\": \"{{ .Function.Name }}\", \"arguments\": {{ .Function.Arguments }}}{{end}}{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}\n{\"name\": \"{{ .Function.Name }}\", \"arguments\": {{ .Function.Arguments }}}{{end}}{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}\r\n{\"name\": \"{{ .Function.Name }}\", \"arguments\": {{ .Function.Arguments }}}{{end}}{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}\n{{range .ToolCalls}}\n{\"name\": \"{{ .Function.Name }}\", \"arguments\": {{ .Function.Arguments }}}\r\n{{end}}\r\n{{end}}", "{")]
    [InlineData("{{if .ToolCalls}}<|tool\u2581calls\u2581begin|>{{range .ToolCalls}}<|tool\u2581call\u2581begin|>functionget_current_weather\n```json\n{\"location\": \"Tokyo\"}\n```<|tool\u2581call\u2581end|>\n{{end}}<|tool\u2581calls\u2581end|>{{end}}", "<|tool\u2581calls\u2581begin|>")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}<tool_call>{\"name\": \"{{ .Function.Name }}\", \"arguments\": {{ .Function.Arguments }}}</tool_call>{{end}}{{end}}", "<tool_call>")]
    [InlineData("{{if .ToolCalls}}\n{{range .ToolCalls}}<tool_call>{\"name\": \"{{ .Function.Name }}\", \"arguments\": {{ .Function.Arguments }}}</tool_call>{{end}}{{end}}", "<tool_call>")]
    [InlineData("{{if .ToolCalls}}{{range .ToolCalls}}<tool_call>{\"name\": \"{{ .Function.Name }}\", \"arguments\": {{ .Function.Arguments }}}<tool_call>{{end}}{{end}}", "<tool_call>")]
    [InlineData("{{if .ToolCalls}}First text{{if .Something}}inner{{end}}Second text{{end}}", "First text")]
    [InlineData("{{if .ToolCalls}}Action: ```json{{end}}", "Action: ```json")]
    [InlineData("{{if .ToolCalls}}functools[{{end}}", "functools[")]
    [InlineData("{{if .ToolCalls}}[TOOL_CALL] [{{end}}", "[TOOL_CALL] [")]
    [InlineData("{{if .ToolCalls}}[TOOL_CALL][{{end}}", "[TOOL_CALL][")]
    public void FromGoTemplateText_reads_the_prefix(string template, string expectedPrefix)
        => ToolCallFormat.FromGoTemplateText(template).Prefix.Should().Be(expectedPrefix);

    /// <summary>A null template is treated as one that says nothing about tool calls.</summary>
    [Fact]
    public void FromGoTemplateText_accepts_null()
    {
        //Act
        ToolCallFormat format = ToolCallFormat.FromGoTemplateText(null);

        //Assert
        format.Prefix.Should().Be("{");
        format.IsBareJson.Should().BeTrue();
    }

    /// <summary>A template that closes its calls gives up the closing literal too.</summary>
    [Fact]
    public void FromGoTemplateText_reads_the_suffix()
    {
        //Arrange
        string template = "{{if .ToolCalls}}<tool_call>{{range .ToolCalls}}{\"name\": \"{{ .Function.Name }}\""
                          + "}{{end}}</tool_call>{{end}}";

        //Act
        ToolCallFormat format = ToolCallFormat.FromGoTemplateText(template);

        //Assert
        format.Prefix.Should().Be("<tool_call>");
        format.Suffix.Should().Be("</tool_call>");
    }

    /// <summary>The Jinja search recognizes each family's tool-call literals.</summary>
    [Theory]
    [InlineData("<tool_call>\n{\"name\": ...}\n</tool_call>", "<tool_call>", "</tool_call>")]
    [InlineData("[TOOL_CALLS] [ ... ][/TOOL_CALLS]", "[TOOL_CALLS]", "[/TOOL_CALLS]")]
    [InlineData("<|python_tag|>{{ call }}<|eom_id|>", "<|python_tag|>", "<|eom_id|>")]
    [InlineData("<function={{ name }}>{{ args }}</function>", "<function=", "</function>")]
    [InlineData("<|start_tool_call|>{{ c }}<|end_tool_call|>", "<|start_tool_call|>", "<|end_tool_call|>")]
    [InlineData("<toolcall>{{ c }}</toolcall>", "<toolcall>", "</toolcall>")]
    [InlineData("functools[{{ c }}]", "functools[", null)]
    public void FromJinjaTemplateText_recognizes_the_known_literals(string template, string expectedPrefix,
        string expectedSuffix)
    {
        //Act
        ToolCallFormat format = ToolCallFormat.FromJinjaTemplateText(template);

        //Assert
        format.Prefix.Should().Be(expectedPrefix);
        if (expectedSuffix == null)
        {
            format.Suffix.Should().BeNull();
        }
        else
        {
            format.Suffix.Should().Be(expectedSuffix);
        }
    }

    /// <summary>A Qwen 3.5 style Jinja template yields the tool_call tag pair.</summary>
    [Fact]
    public void FromJinjaTemplateText_handles_a_qwen_template()
    {
        //Arrange
        string template = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "qwen3-tools.jinja"));

        //Act
        ToolCallFormat format = ToolCallFormat.FromJinjaTemplateText(template);

        //Assert
        format.Prefix.Should().Be("<tool_call>");
        format.Suffix.Should().Be("</tool_call>");
        format.IsBareJson.Should().BeFalse();
    }

    /// <summary>A Jinja template with no tool-call literal falls back to bare JSON detection.</summary>
    [Fact]
    public void FromJinjaTemplateText_falls_back_to_bare_json()
    {
        //Act
        ToolCallFormat format =
            ToolCallFormat.FromJinjaTemplateText("{% for m in messages %}{{ m.content }}{% endfor %}");

        //Assert
        format.Prefix.Should().Be("{");
        format.IsBareJson.Should().BeTrue();
    }

    /// <summary>The prefix and suffix are read off the text a template rendered for one synthetic call.</summary>
    [Theory]
    [InlineData("<tool_call>{\"name\": \"x\", \"arguments\": {}}</tool_call>", "<tool_call>", "</tool_call>")]
    [InlineData("[TOOL_CALLS] [{\"name\": \"x\", \"arguments\": {}}]", "[TOOL_CALLS] [", "]")]
    [InlineData("{\"name\": \"x\", \"arguments\": {}}", "{", null)]
    [InlineData("[{\"name\": \"x\", \"arguments\": {}}]", "[", "]")]
    [InlineData("no call here at all", "{", null)]
    public void FromRenderedToolCall_reads_the_prefix(string rendered, string expectedPrefix,
        string expectedSuffix)
    {
        //Act
        ToolCallFormat format = ToolCallFormat.FromRenderedToolCall(rendered);

        //Assert
        format.Prefix.Should().Be(expectedPrefix);
        if (expectedSuffix == null)
        {
            format.Suffix.Should().BeNull();
        }
        else
        {
            format.Suffix.Should().Be(expectedSuffix);
        }
    }

    /// <summary>The automatic format looks for bare JSON and nothing else.</summary>
    [Fact]
    public void Auto_is_bare_json_detection()
    {
        //Assert
        ToolCallFormat.Auto.Prefix.Should().Be("{");
        ToolCallFormat.Auto.Suffix.Should().BeNull();
        ToolCallFormat.Auto.IsBareJson.Should().BeTrue();
    }

    /// <summary>An empty prefix means bare JSON, and a literal prefix does not.</summary>
    [Theory]
    [InlineData(null, "{", true)]
    [InlineData("", "{", true)]
    [InlineData("[", "[", true)]
    [InlineData("<tool_call>", "<tool_call>", false)]
    public void Prefix_is_never_empty(string prefix, string expectedPrefix, bool expectedBareJson)
    {
        //Arrange
        ToolCallFormat format = new ToolCallFormat(prefix);

        //Assert
        format.Prefix.Should().Be(expectedPrefix);
        format.IsBareJson.Should().Be(expectedBareJson);
    }
}
