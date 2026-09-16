using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The variables a Jinja chat template sees, built from a request: the shape is the contract between this
/// library and every model author who wrote a template against the transformers convention.
/// </summary>
public sealed class ChatJinjaVariablesTests
{
    /// <summary>The fixed variables are always there and a generation prompt is always asked for.</summary>
    [Fact]
    public void Build_always_asks_for_a_generation_prompt_and_carries_the_tokens()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        IReadOnlyDictionary<string, object> variables = ChatJinjaVariables.Build(request, "<s>", "</s>");

        //Assert
        variables["add_generation_prompt"].Should().Be(true);
        variables["bos_token"].Should().Be("<s>");
        variables["eos_token"].Should().Be("</s>");
        variables.ContainsKey("enable_thinking").Should().BeFalse();
        variables.ContainsKey("tools").Should().BeFalse();
    }

    /// <summary>A request with no opinion about thinking leaves the template's own default standing.</summary>
    [Fact]
    public void Build_adds_enable_thinking_only_when_the_request_has_an_opinion()
    {
        //Arrange
        ChatRequest request = new ChatRequest { Think = false };
        request.Messages.Add(new ChatMessage(ChatRole.User, "hello"));

        //Act
        IReadOnlyDictionary<string, object> variables = ChatJinjaVariables.Build(request, "", "");

        //Assert
        variables["enable_thinking"].Should().Be(false);
    }

    /// <summary>Every message field the convention names appears when the message carries it.</summary>
    [Fact]
    public void Build_maps_every_message_field()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.System, "be brief"));
        request.Messages.Add(new ChatMessage(ChatRole.User, "weather in Paris?"));

        ChatMessage assistant = new ChatMessage(ChatRole.Assistant, "") { Thinking = "the user wants weather" };
        assistant.ToolCalls.Add(new ToolCall
        {
            Id = "call_1",
            Name = "get_weather",
            ArgumentsJson = "{\"city\":\"Paris\"}",
        });

        request.Messages.Add(assistant);
        request.Messages.Add(new ChatMessage(ChatRole.Tool, "sunny")
        {
            ToolCallId = "call_1",
            ToolName = "get_weather",
        });

        //Act
        IReadOnlyDictionary<string, object> variables = ChatJinjaVariables.Build(request, "", "<|im_end|>");

        //Assert
        IList<object> messages = (IList<object>)variables["messages"];
        messages.Should().HaveCount(4);

        Mapping(messages[0])["role"].Should().Be("system");
        Mapping(messages[1])["content"].Should().Be("weather in Paris?");

        IDictionary<string, object> third = Mapping(messages[2]);
        third["role"].Should().Be("assistant");
        third["reasoning_content"].Should().Be("the user wants weather");

        IList<object> calls = (IList<object>)third["tool_calls"];
        calls.Should().HaveCount(1);

        IDictionary<string, object> call = Mapping(calls[0]);
        call["type"].Should().Be("function");
        call["id"].Should().Be("call_1");

        IDictionary<string, object> function = Mapping(call["function"]);
        function["name"].Should().Be("get_weather");
        Mapping(function["arguments"])["city"].Should().Be("Paris");

        IDictionary<string, object> fourth = Mapping(messages[3]);
        fourth["role"].Should().Be("tool");
        fourth["tool_call_id"].Should().Be("call_1");
        fourth["name"].Should().Be("get_weather");
    }

    /// <summary>Arguments the model wrote as something other than a JSON object stay as the text they were.</summary>
    [Fact]
    public void Build_keeps_arguments_that_are_not_a_json_object_as_text()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        ChatMessage assistant = new ChatMessage(ChatRole.Assistant, "");
        assistant.ToolCalls.Add(new ToolCall { Name = "shout", ArgumentsJson = "not json at all" });
        request.Messages.Add(assistant);

        //Act
        IReadOnlyDictionary<string, object> variables = ChatJinjaVariables.Build(request, "", "");

        //Assert
        IList<object> messages = (IList<object>)variables["messages"];
        IList<object> calls = (IList<object>)Mapping(messages[0])["tool_calls"];
        Mapping(Mapping(calls[0])["function"])["arguments"].Should().Be("not json at all");
    }

    /// <summary>Tools arrive in the OpenAI shape with their schema parsed back into a mapping.</summary>
    [Fact]
    public void Build_maps_tools_into_the_openai_shape()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "weather?"));
        request.Tools.Add(new ToolDefinition
        {
            Name = "get_weather",
            Description = "Get the weather.",
            ParametersJsonSchema =
                "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}",
        });

        //Act
        IReadOnlyDictionary<string, object> variables = ChatJinjaVariables.Build(request, "", "");

        //Assert
        IList<object> tools = (IList<object>)variables["tools"];
        tools.Should().HaveCount(1);

        IDictionary<string, object> tool = Mapping(tools[0]);
        tool["type"].Should().Be("function");

        IDictionary<string, object> function = Mapping(tool["function"]);
        function["name"].Should().Be("get_weather");
        function["description"].Should().Be("Get the weather.");

        IDictionary<string, object> parameters = Mapping(function["parameters"]);
        parameters["type"].Should().Be("object");
        Mapping(parameters["properties"]).ContainsKey("city").Should().BeTrue();
    }

    /// <summary>A tool with no schema is offered as one that takes an empty object.</summary>
    [Fact]
    public void Build_gives_a_tool_with_no_schema_the_empty_object_schema()
    {
        //Arrange
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "now?"));
        request.Tools.Add(new ToolDefinition { Name = "now" });

        //Act
        IReadOnlyDictionary<string, object> variables = ChatJinjaVariables.Build(request, "", "");

        //Assert
        IList<object> tools = (IList<object>)variables["tools"];
        IDictionary<string, object> function = Mapping(Mapping(tools[0])["function"]);
        function.ContainsKey("description").Should().BeFalse();

        IDictionary<string, object> parameters = Mapping(function["parameters"]);
        parameters["type"].Should().Be("object");
        Mapping(parameters["properties"]).Should().BeEmpty();
    }

    private static IDictionary<string, object> Mapping(object value) => (IDictionary<string, object>)value;
}
