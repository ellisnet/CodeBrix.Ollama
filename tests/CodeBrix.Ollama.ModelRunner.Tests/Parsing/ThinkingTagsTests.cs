using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the tag inference against every case in Ollama's own template tests, and the Jinja string search
/// this library adds for templates that are not Go templates.
/// </summary>
public sealed class ThinkingTagsTests
{
    private const string BasicTemplate =
        "\n\t\t\t{{ if .Thinking}}\n\t\t\t\t/think\n\t\t\t{{ end }}\n"
        + "\t\t\t{{- range $i, $_ := .Messages }}\n"
        + "\t\t\t\t{{- $last := eq (len (slice $.Messages $i)) 1 -}}\n"
        + "\t\t\t\t{{ if and $last .Thinking }}\n"
        + "\t\t\t\t\t<think>{{ .Thinking }}</think>\n"
        + "\t\t\t\t{{ end }}\n\t\t\t{{ end }}\n\t\t";

    private const string DoublyNestedRangeTemplate =
        "\n\t\t\t{{ if .Thinking}}\n\t\t\t\t/think\n\t\t\t{{ end }}\n"
        + "\t\t\t{{- range $i, $_ := .Messages }}\n"
        + "\t\t\t\t{{- range $j, $_ := .NotMessages }}\n"
        + "\t\t\t\t\t{{- $last := eq (len (slice $.Messages $i)) 1 -}}\n"
        + "\t\t\t\t\t{{ if and $last .Thinking }}\n"
        + "\t\t\t\t\t\t<think>{{ .Thinking }}</think>\n"
        + "\t\t\t\t\t{{ end }}\n\t\t\t\t{{ end }}\n\t\t\t{{ end }}\n\t\t";

    private const string TrimmedWhitespaceTemplate =
        "\n\t\t\t{{ if .Thinking}}\n\t\t\t\t/think\n\t\t\t{{ end }}\n"
        + "\t\t\t{{- range $i, $_ := .Messages }}\n"
        + "\t\t\t\t{{- $last := eq (len (slice $.Messages $i)) 1 -}}\n"
        + "\t\t\t\t{{ if and $last .Thinking }}\n"
        + "\t\t\t\t\tSome text before   {{ .Thinking }}    Some text after\n"
        + "\t\t\t\t{{ end }}\n\t\t\t{{ end }}\n\t\t";

    /// <summary>A template that emits or strips tagged reasoning is recognized as a thinking template.</summary>
    [Theory]
    [InlineData("<think>{{ reasoning }}</think>", true)]
    [InlineData("content.split('</think>')", true)]
    [InlineData("content.split(\"</think>\")", true)]
    [InlineData("content.split('</think>') reasoning_content", false)]
    [InlineData("content.split('</think>') <SPECIAL_12>", false)]
    [InlineData("{{ content }}", false)]
    public void TemplateSupportsThinking_recognizes_tagged_reasoning(string template, bool expected)
        => ThinkingTags.TemplateSupportsThinking(template).Should().Be(expected);

    /// <summary>A null template is treated as one that says nothing about thinking.</summary>
    [Fact]
    public void TemplateSupportsThinking_accepts_null()
        => ThinkingTags.TemplateSupportsThinking(null).Should().BeFalse();

    /// <summary>The Go heuristic finds the tags around the reasoning of the last message.</summary>
    [Theory]
    [InlineData(nameof(BasicTemplate), "<think>", "</think>")]
    [InlineData(nameof(DoublyNestedRangeTemplate), "", "")]
    [InlineData(nameof(TrimmedWhitespaceTemplate), "Some text before", "Some text after")]
    public void InferGoTemplateTags_finds_the_tags(string templateName, string expectedOpening,
        string expectedClosing)
    {
        //Arrange
        string template = TemplateByName(templateName);

        //Act
        bool found = ThinkingTags.InferGoTemplateTags(template, out string opening, out string closing);

        //Assert
        opening.Should().Be(expectedOpening);
        closing.Should().Be(expectedClosing);
        found.Should().Be(expectedOpening.Length > 0);
    }

    /// <summary>Ollama's own Qwen 3 Go template yields the think tags.</summary>
    [Fact]
    public void InferGoTemplateTags_handles_the_qwen3_template()
    {
        //Arrange
        string template = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "qwen3.gotmpl"));

        //Act
        bool found = ThinkingTags.InferGoTemplateTags(template, out string opening, out string closing);

        //Assert
        found.Should().BeTrue();
        opening.Should().Be("<think>");
        closing.Should().Be("</think>");
    }

    /// <summary>The Jinja search recognizes each family's reasoning literals.</summary>
    [Theory]
    [InlineData("{%- if enable_thinking %}<think>\n{{ reasoning }}\n</think>{%- endif %}", "<think>", "</think>")]
    [InlineData("<|START_THINKING|>{{ r }}<|END_THINKING|>", "<|START_THINKING|>", "<|END_THINKING|>")]
    [InlineData("<|channel|>analysis<|message|>{{ r }}<|end|>", "<|channel|>analysis<|message|>", "<|end|>")]
    [InlineData("<seed:think>{{ r }}</seed:think>", "<seed:think>", "</seed:think>")]
    [InlineData("<reasoning>{{ r }}</reasoning>", "<reasoning>", "</reasoning>")]
    [InlineData("<thought>{{ r }}</thought>", "<thought>", "</thought>")]
    [InlineData("{{ content.split('</think>')[-1] }}", "<think>", "</think>")]
    public void InferJinjaTemplateTags_recognizes_the_known_literals(string template,
        string expectedOpening, string expectedClosing)
    {
        //Act
        bool found = ThinkingTags.InferJinjaTemplateTags(template, out string opening, out string closing);

        //Assert
        found.Should().BeTrue();
        opening.Should().Be(expectedOpening);
        closing.Should().Be(expectedClosing);
    }

    /// <summary>A template with no reasoning literal at all yields nothing.</summary>
    [Fact]
    public void InferJinjaTemplateTags_finds_nothing_in_a_plain_template()
    {
        //Act
        bool found = ThinkingTags.InferJinjaTemplateTags("{% for m in messages %}{{ m.content }}{% endfor %}",
            out string opening, out string closing);

        //Assert
        found.Should().BeFalse();
        opening.Should().BeEmpty();
        closing.Should().BeEmpty();
    }

    /// <summary>The dialect-independent entry point picks the right heuristic for each dialect.</summary>
    [Fact]
    public void Infer_picks_the_heuristic_that_suits_the_template()
    {
        //Arrange
        string jinja = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "qwen3-tools.jinja"));

        //Act
        bool foundInGo = ThinkingTags.Infer(BasicTemplate, out string goOpening, out string goClosing);
        bool foundInJinja = ThinkingTags.Infer(jinja, out string jinjaOpening, out string jinjaClosing);

        //Assert
        foundInGo.Should().BeTrue();
        goOpening.Should().Be("<think>");
        goClosing.Should().Be("</think>");
        foundInJinja.Should().BeTrue();
        jinjaOpening.Should().Be("<think>");
        jinjaClosing.Should().Be("</think>");
    }

    /// <summary>A prompt that already opened the thinking block is detected.</summary>
    [Theory]
    [InlineData("<|im_start|>assistant\n<think>", "<think>", true)]
    [InlineData("<|im_start|>assistant\n<think>\n\n  ", "<think>", true)]
    [InlineData("<|im_start|>assistant\n", "<think>", false)]
    [InlineData("<think>reasoning already", "<think>", false)]
    [InlineData("", "<think>", false)]
    [InlineData("<|im_start|>assistant\n<think>", "", false)]
    public void PromptEndsWithOpeningTag_detects_an_already_open_block(string prompt, string openingTag,
        bool expected)
        => ThinkingTags.PromptEndsWithOpeningTag(prompt, openingTag).Should().Be(expected);

    /// <summary>Returns one of the Go templates the theories name.</summary>
    /// <param name="name">The template name.</param>
    /// <returns>The template source.</returns>
    private static string TemplateByName(string name)
    {
        switch (name)
        {
            case nameof(BasicTemplate):
                return BasicTemplate;
            case nameof(DoublyNestedRangeTemplate):
                return DoublyNestedRangeTemplate;
            default:
                return TrimmedWhitespaceTemplate;
        }
    }
}
