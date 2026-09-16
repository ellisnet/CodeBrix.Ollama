using System.Collections.Generic;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The post-processing a chat reply goes through, driven over a scripted token stream rather than a model:
/// reasoning is separated first, tool calls are read out of what is left, and the calls of a turn are
/// numbered.
/// </summary>
public sealed class ChatPipelineTests
{
    private const string JsonToolTemplate =
        "<think></think><tool_call>{\"name\": \"x\", \"arguments\": {}}</tool_call>";

    private const string FunctionToolTemplate =
        "<think></think><tool_call>\n<function=x>\n<parameter=p>\nv\n</parameter>\n</function>\n</tool_call>";

    private const string PlainTemplate = "<|im_start|>{{ role }}<|im_end|>";

    private const string SwitchableTemplate =
        "{% if enable_thinking %}<think>\n\n</think>\n\n{% endif %}"
        + "<tool_call>{\"name\": \"x\", \"arguments\": {}}</tool_call>";

    /// <summary>A reasoning block and a JSON tool call are separated out of one stream.</summary>
    [Fact]
    public void Add_separates_thinking_from_content_and_reads_a_json_tool_call()
    {
        //Arrange
        ChatRequest request = NewRequest();
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, JsonToolTemplate, "<|im_start|>assistant\n");

        //Act
        Transcript transcript = Drive(pipeline,
            "<think>", "plan", "</think>", "Hello ", "<tool_call>",
            "{\"name\": \"get_weather\", \"arguments\": {\"city\": \"Paris\"}}", "</tool_call>");

        //Assert
        transcript.Thinking.Should().Be("plan");
        transcript.Content.Trim().Should().Be("Hello");
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].Id.Should().Be("call_1");
        transcript.Calls[0].Name.Should().Be("get_weather");
        transcript.Calls[0].ArgumentsJson.Should().Contain("Paris");
        pipeline.ToolCalls.Should().HaveCount(1);
    }

    /// <summary>The calls of one turn are numbered from one, in the order the model wrote them.</summary>
    [Fact]
    public void Add_numbers_every_call_of_a_turn_from_one()
    {
        //Arrange
        ChatRequest request = NewRequest();
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, JsonToolTemplate, "<|im_start|>assistant\n");

        //Act
        Transcript transcript = Drive(pipeline,
            "<tool_call>{\"name\": \"get_weather\", \"arguments\": {\"city\": \"Paris\"}}</tool_call>",
            "<tool_call>{\"name\": \"get_weather\", \"arguments\": {\"city\": \"Rome\"}}</tool_call>");

        //Assert
        transcript.Calls.Should().HaveCount(2);
        transcript.Calls[0].Id.Should().Be("call_1");
        transcript.Calls[1].Id.Should().Be("call_2");
        transcript.Calls[1].ArgumentsJson.Should().Contain("Rome");
    }

    /// <summary>A model that writes its calls as nested tags is understood too.</summary>
    [Fact]
    public void Add_reads_a_tool_call_written_as_nested_tags()
    {
        //Arrange
        ChatRequest request = NewRequest();
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, FunctionToolTemplate, "<|im_start|>assistant\n<think>");

        //Act
        Transcript transcript = Drive(pipeline,
            "reasoning", "</think>", "\n\n", "<tool_call>\n<function=get_weather>\n",
            "<parameter=city>\nParis\n</parameter>\n", "</function>\n</tool_call>");

        //Assert
        transcript.Thinking.Should().Be("reasoning");
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].Name.Should().Be("get_weather");
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\"}");
        transcript.Content.Trim().Should().BeEmpty();
    }

    /// <summary>A prompt that ends with the opening tag means the model is already reasoning.</summary>
    [Fact]
    public void Create_starts_inside_thinking_when_the_prompt_ends_with_the_opening_tag()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, JsonToolTemplate, "<|im_start|>assistant\n<think>\n");
        Transcript transcript = Drive(pipeline, "plan", "</think>", "answer");

        //Assert
        pipeline.ParsesThinking.Should().BeTrue();
        pipeline.ParsesToolCalls.Should().BeFalse();
        transcript.Thinking.Should().Be("plan");
        transcript.Content.Should().Be("answer");
    }

    /// <summary>A request that turned thinking off gets no reasoning stage at all.</summary>
    [Fact]
    public void Create_skips_the_thinking_stage_when_the_request_turned_it_off()
    {
        //Arrange
        ChatRequest request = new ChatRequest { Think = false };
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        ChatPipeline pipeline = ChatPipeline.Create(
            request,
            ChatTemplateDialect.Jinja,
            SwitchableTemplate,
            "<|im_start|>assistant\n<think>\n\n</think>\n\n");

        Transcript transcript = Drive(pipeline, "Earth");

        //Assert
        pipeline.ParsesThinking.Should().BeFalse();
        transcript.Thinking.Should().BeEmpty();
        transcript.Content.Should().Be("Earth");
    }

    /// <summary>A template with the tags but no switch keeps the stage, because the "no" reached nothing.</summary>
    [Fact]
    public void Create_keeps_the_thinking_stage_when_the_template_has_no_switch_to_turn_it_off()
    {
        //Arrange
        ChatRequest request = new ChatRequest { Think = false };
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, JsonToolTemplate, "<|im_start|>assistant\n");

        Transcript transcript = Drive(pipeline, "<think>", "plan", "</think>", "Earth");

        //Assert
        pipeline.ParsesThinking.Should().BeTrue();
        transcript.Thinking.Should().Be("plan");
        transcript.Content.Should().Be("Earth");
    }

    /// <summary>Thinking off but a prompt left open still gets the stage, because the model is reasoning.</summary>
    [Fact]
    public void Create_keeps_the_thinking_stage_when_the_prompt_left_the_block_open()
    {
        //Arrange
        ChatRequest request = new ChatRequest { Think = false };
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, JsonToolTemplate, "<|im_start|>assistant\n<think>\n");

        //Assert
        pipeline.ParsesThinking.Should().BeTrue();
    }

    /// <summary>A template with no reasoning tags leaves the stream alone.</summary>
    [Fact]
    public void Create_skips_both_stages_for_a_plain_template_and_no_tools()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, PlainTemplate, "<|im_start|>assistant\n");
        Transcript transcript = Drive(pipeline, "<think>not a tag</think> and ", "<tool_call>not a call");

        //Assert
        pipeline.ParsesThinking.Should().BeFalse();
        pipeline.ParsesToolCalls.Should().BeFalse();
        transcript.Content.Should().Be("<think>not a tag</think> and <tool_call>not a call");
        transcript.Calls.Should().BeEmpty();
    }

    /// <summary>A call naming a tool that was never offered is text, not a call.</summary>
    [Fact]
    public void Add_treats_a_call_to_an_unoffered_tool_as_content()
    {
        //Arrange
        ChatRequest request = NewRequest();
        ChatPipeline pipeline = ChatPipeline.Create(
            request, ChatTemplateDialect.Jinja, FunctionToolTemplate, "<|im_start|>assistant\n");

        //Act
        Transcript transcript = Drive(pipeline,
            "<tool_call>\n<function=launch_rocket>\n<parameter=where>\nspace\n</parameter>\n</function>\n</tool_call>");

        //Assert
        transcript.Calls.Should().BeEmpty();
        transcript.Content.Should().Contain("launch_rocket");
    }

    private static ChatRequest NewRequest()
    {
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "What is the weather in Paris?"));
        request.Tools.Add(new ToolDefinition
        {
            Name = "get_weather",
            Description = "Get the weather for a city.",
            ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}}}",
        });

        return request;
    }

    private static Transcript Drive(ChatPipeline pipeline, params string[] chunks)
    {
        StringBuilder thinking = new StringBuilder();
        StringBuilder content = new StringBuilder();
        List<ToolCall> calls = new List<ToolCall>();

        foreach (string chunk in chunks)
        {
            ChatPipelineOutput step = pipeline.Add(chunk);
            thinking.Append(step.Thinking);
            content.Append(step.Content);
            calls.AddRange(step.ToolCalls);
        }

        ChatPipelineOutput tail = pipeline.Flush();
        thinking.Append(tail.Thinking);
        content.Append(tail.Content);
        calls.AddRange(tail.ToolCalls);

        return new Transcript(thinking.ToString(), content.ToString(), calls);
    }

    private sealed class Transcript
    {
        public Transcript(string thinking, string content, IReadOnlyList<ToolCall> calls)
        {
            Thinking = thinking;
            Content = content;
            Calls = calls;
        }

        public string Thinking { get; }

        public string Content { get; }

        public IReadOnlyList<ToolCall> Calls { get; }
    }
}
