using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Tests for parsing an Ollama chat template and for the set of variables it reports, which is what
/// decides whether a template is rendered in its message form or its older prompt-and-response form.
/// </summary>
public sealed class OllamaTemplateParseTests
{
    /// <summary>A template reports exactly the lower-cased identifiers Ollama reports for it.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected variables, comma separated.</param>
    [Theory]
    [InlineData("{{ .Prompt }}", "prompt,response")]
    [InlineData("{{ .System }} {{ .Prompt }}", "prompt,response,system")]
    [InlineData("{{ .System }} {{ .Prompt }} {{ .Response }}", "prompt,response,system")]
    [InlineData("{{ with .Tools }}{{ . }}{{ end }} {{ .System }} {{ .Prompt }}", "prompt,response,system,tools")]
    [InlineData("{{ range .Messages }}{{ .Role }} {{ .Content }}{{ end }}", "content,messages,role")]
    [InlineData("{{ range .Messages }}{{ if eq .Role \"tool\" }}Tool Result: {{ .ToolName }} {{ .Content }}{{ end }}{{ end }}",
        "content,messages,role,toolname")]
    public void Variables_lists_the_identifiers_the_template_uses(string template, string expected)
    {
        //Act
        var parsed = OllamaTemplate.Parse(template);

        //Assert
        string.Join(",", parsed.Variables).Should().Be(expected);
    }

    /// <summary>A multi-line role switch reports its identifiers.</summary>
    [Fact]
    public void Variables_with_a_multiline_role_switch()
    {
        //Arrange
        const string template = "{{- range .Messages }}\n"
            + "{{- if eq .Role \"system\" }}SYSTEM:\n"
            + "{{- else if eq .Role \"user\" }}USER:\n"
            + "{{- else if eq .Role \"assistant\" }}ASSISTANT:\n"
            + "{{- else if eq .Role \"tool\" }}TOOL:\n"
            + "{{- end }} {{ .Content }}\n"
            + "{{- end }}";

        //Act
        var parsed = OllamaTemplate.Parse(template);

        //Assert
        string.Join(",", parsed.Variables).Should().Be("content,messages,role");
    }

    /// <summary>A ChatML-shaped template that carries both forms reports the identifiers of both.</summary>
    [Fact]
    public void Variables_with_both_template_forms()
    {
        //Arrange
        const string template = "{{- if .Messages }}\n"
            + "{{- range .Messages }}<|im_start|>{{ .Role }}\n"
            + "{{ .Content }}<|im_end|>\n"
            + "{{ end }}<|im_start|>assistant\n"
            + "{{ else -}}\n"
            + "{{ if .System }}<|im_start|>system\n"
            + "{{ .System }}<|im_end|>\n"
            + "{{ end }}{{ if .Prompt }}<|im_start|>user\n"
            + "{{ .Prompt }}<|im_end|>\n"
            + "{{ end }}<|im_start|>assistant\n"
            + "{{ .Response }}<|im_end|>\n"
            + "{{- end -}}";

        //Act
        var parsed = OllamaTemplate.Parse(template);

        //Assert
        string.Join(",", parsed.Variables).Should().Be("content,messages,prompt,response,role,system");
    }

    /// <summary>An unclosed action is rejected.</summary>
    [Fact]
    public void Parse_with_an_unclosed_action_throws()
    {
        //Act
        var thrown = Record.Exception(() => OllamaTemplate.Parse("{{ .Prompt "));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.ToLowerInvariant().Should().Contain("unclosed action");
    }

    /// <summary>A template invocation with no pipeline is rejected, as it is by Ollama.</summary>
    [Fact]
    public void Parse_with_an_undefined_template_pipeline_throws()
    {
        //Act
        var thrown = Record.Exception(()
            => OllamaTemplate.Parse("{{define \"x\"}}{{template \"x\"}}{{end}}{{template \"x\"}}"));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.ToLowerInvariant().Should().Contain("undefined template specified");
    }

    /// <summary>A call to a template that is never defined parses cleanly and fails while rendering.</summary>
    [Fact]
    public void Render_with_an_undefined_template_call_throws_at_render_time()
    {
        //Arrange
        var parsed = OllamaTemplate.Parse("{{ template \"missing\" . }}");

        //Act
        var thrown = Record.Exception(() => parsed.Render(new OllamaTemplateValues()));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.Should().Contain("not defined");
    }

    /// <summary>The source text a template was parsed from is kept as it was given.</summary>
    [Fact]
    public void Source_keeps_the_original_text()
    {
        //Arrange
        const string template = "{{ .Prompt }}";

        //Act
        var parsed = OllamaTemplate.Parse(template);

        //Assert
        parsed.Source.Should().Be(template);
    }
}
