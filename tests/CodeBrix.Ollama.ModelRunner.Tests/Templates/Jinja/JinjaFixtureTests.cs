using System.Collections.Generic;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Renders the real chat templates under Fixtures/Jinja and compares the result with the recorded
/// expected text, byte for byte. Fixtures/Jinja/SOURCES.txt records where each template came from and
/// under which license.
/// </summary>
public sealed class JinjaFixtureTests
{
    /// <summary>Every fixture case renders exactly the recorded text.</summary>
    /// <param name="templateName">The short template name.</param>
    /// <param name="caseName">The case name.</param>
    [Theory]
    [InlineData("qwen3.5-35b-a3b", "user")]
    [InlineData("qwen3.5-35b-a3b", "nothink")]
    [InlineData("qwen3.5-35b-a3b", "tools")]
    [InlineData("qwen3.5-35b-a3b", "toolcall")]
    [InlineData("qwen3.5-35b-a3b", "reasoning")]
    [InlineData("qwen3.5-35b-a3b", "multimodal")]
    [InlineData("qwen3-0.6b", "basic")]
    [InlineData("qwen3-0.6b", "nothink")]
    [InlineData("qwen3-0.6b", "tools")]
    [InlineData("qwen2.5-7b", "basic")]
    [InlineData("qwen2.5-7b", "defaultsystem")]
    [InlineData("qwen2.5-7b", "tools")]
    [InlineData("smollm2-360m", "basic")]
    [InlineData("smollm2-360m", "nosystem")]
    [InlineData("mistral-7b-v0.3", "basic")]
    [InlineData("mistral-7b-v0.3", "tools")]
    [InlineData("phi-4-mini", "basic")]
    [InlineData("phi-4-mini", "tools")]
    [InlineData("phi-3.5-mini", "basic")]
    [InlineData("deepseek-r1-distill-qwen-1.5b", "basic")]
    [InlineData("deepseek-r1-distill-qwen-1.5b", "thinking")]
    [InlineData("deepseek-r1-distill-qwen-1.5b", "toolcall")]
    [InlineData("granite-3.3-2b", "basic")]
    [InlineData("granite-3.3-2b", "documents")]
    [InlineData("olmo-2-7b", "basic")]
    public void Render_reproduces_the_recorded_fixture(string templateName, string caseName)
    {
        //Arrange
        JinjaTemplate template = JinjaFixtureLoader.LoadTemplate(templateName);
        IReadOnlyDictionary<string, object> variables =
            JinjaFixtureLoader.LoadVariables(templateName, caseName);

        //Act
        string rendered = template.Render(variables);

        //Assert
        rendered.Should().Be(JinjaFixtureLoader.LoadExpected(templateName, caseName));
    }

    /// <summary>Every fixture template parses, and keeps its source unchanged.</summary>
    /// <param name="templateName">The short template name.</param>
    [Theory]
    [InlineData("qwen3.5-35b-a3b")]
    [InlineData("qwen3-0.6b")]
    [InlineData("qwen2.5-7b")]
    [InlineData("smollm2-360m")]
    [InlineData("mistral-7b-v0.3")]
    [InlineData("phi-4-mini")]
    [InlineData("phi-3.5-mini")]
    [InlineData("deepseek-r1-distill-qwen-1.5b")]
    [InlineData("granite-3.3-2b")]
    [InlineData("olmo-2-7b")]
    public void Parse_accepts_every_fixture_template(string templateName)
    {
        //Arrange
        string source = File.ReadAllText(Path.Combine(JinjaFixtureLoader.Directory, templateName + ".jinja"));

        //Act
        JinjaTemplate template = JinjaTemplate.Parse(source);

        //Assert
        template.Source.Should().Be(source);
    }

    /// <summary>A plain user turn with a generation prompt renders the Qwen 3.5 ChatML opening.</summary>
    [Fact]
    public void Render_qwen35_plain_user_turn_opens_the_assistant_block()
    {
        //Act
        string rendered = JinjaFixtureLoader.LoadTemplate("qwen3.5-35b-a3b")
            .Render(JinjaFixtureLoader.LoadVariables("qwen3.5-35b-a3b", "user"));

        //Assert
        rendered.Should().Be("<|im_start|>user\nHello!<|im_end|>\n<|im_start|>assistant\n<think>\n");
    }

    /// <summary>Turning thinking off adds the empty think block the template writes.</summary>
    [Fact]
    public void Render_qwen35_with_thinking_off_emits_an_empty_think_block()
    {
        //Act
        string rendered = JinjaFixtureLoader.LoadTemplate("qwen3.5-35b-a3b")
            .Render(JinjaFixtureLoader.LoadVariables("qwen3.5-35b-a3b", "nothink"));

        //Assert
        rendered.Should().EndWith("<|im_start|>assistant\n<think>\n\n</think>\n\n");
    }

    /// <summary>Tools render into a system turn holding the tools block and the call instructions.</summary>
    [Fact]
    public void Render_qwen35_with_tools_emits_the_tools_block()
    {
        //Act
        string rendered = JinjaFixtureLoader.LoadTemplate("qwen3.5-35b-a3b")
            .Render(JinjaFixtureLoader.LoadVariables("qwen3.5-35b-a3b", "tools"));

        //Assert
        rendered.Should().StartWith("<|im_start|>system\n# Tools\n");
        rendered.Should().Contain("<tools>\n{\"type\": \"function\", \"function\": {\"name\": \"get_weather\"");
        rendered.Should().Contain("\n</tools>\n");
        rendered.Should().Contain("<tool_call>\n<function=example_function_name>\n");
        rendered.Should().Contain("\n\nYou are a careful assistant.<|im_end|>\n");
    }

    /// <summary>A tool call and its result render as the function and tool_response blocks.</summary>
    [Fact]
    public void Render_qwen35_with_a_tool_call_emits_the_function_and_response_blocks()
    {
        //Act
        string rendered = JinjaFixtureLoader.LoadTemplate("qwen3.5-35b-a3b")
            .Render(JinjaFixtureLoader.LoadVariables("qwen3.5-35b-a3b", "toolcall"));

        //Assert
        rendered.Should().Contain(
            "<tool_call>\n<function=get_weather>\n<parameter=city>\nParis\n</parameter>\n"
            + "<parameter=options>\n{\"unit\": \"celsius\"}\n</parameter>\n</function>\n</tool_call><|im_end|>\n");
        rendered.Should().Contain(
            "<|im_start|>user\n<tool_response>\n{\"temperature\": 18}\n</tool_response><|im_end|>\n");
    }

    /// <summary>Reasoning is dropped before the last user query and kept after it.</summary>
    [Fact]
    public void Render_qwen35_keeps_reasoning_only_after_the_last_user_query()
    {
        //Act
        string rendered = JinjaFixtureLoader.LoadTemplate("qwen3.5-35b-a3b")
            .Render(JinjaFixtureLoader.LoadVariables("qwen3.5-35b-a3b", "reasoning"));

        //Assert
        rendered.Should().Contain("<|im_start|>assistant\nAnswer one.<|im_end|>\n");
        rendered.Should().NotContain("Thinking about one.");
        rendered.Should().Contain(
            "<|im_start|>assistant\n<think>\nThinking about two.\n</think>\n\nAnswer two.<|im_end|>\n");
    }
}
