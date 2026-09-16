using System.Collections.Generic;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Reading the nested-tag tool-call syntax Qwen 3.5 writes, which is not JSON and which the JSON parser
/// cannot see.
/// </summary>
public sealed class ChatFunctionCallParserTests
{
    /// <summary>A template teaching the nested tags is recognized as one; a JSON one is not.</summary>
    [Theory]
    [InlineData("<tool_call>\n<function=x>\n<parameter=y>\n</parameter>\n</function>\n</tool_call>", true)]
    [InlineData("<tool_call>{\"name\": \"x\", \"arguments\": {}}</tool_call>", false)]
    [InlineData("<function=x></function>", false)]
    [InlineData(null, false)]
    public void TemplateUsesFunctionSyntax_recognizes_the_nested_tag_convention(string template, bool expected)
        => ChatFunctionCallParser.TemplateUsesFunctionSyntax(template).Should().Be(expected);

    /// <summary>One call arrives whole, with its parameters as a JSON object.</summary>
    [Fact]
    public void AddContent_reads_one_call_with_its_parameters()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\nParis\n</parameter>\n"
            + "<parameter=days>\n3\n</parameter>\n</function>\n</tool_call>");

        //Assert
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].Name.Should().Be("get_weather");
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\",\"days\":3}");
        transcript.Content.Should().BeEmpty();
        parser.CallCount.Should().Be(1);
    }

    /// <summary>A parameter the schema calls a string stays a string even when it looks like a number.</summary>
    [Fact]
    public void AddContent_keeps_a_declared_string_parameter_a_string()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\n12345\n</parameter>\n"
            + "</function>\n</tool_call>");

        //Assert
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"12345\"}");
    }

    /// <summary>A parameter whose value is a JSON object keeps its structure.</summary>
    [Fact]
    public void AddContent_keeps_a_json_valued_parameter_as_json()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=options>\n{\"unit\": \"celsius\"}\n"
            + "</parameter>\n</function>\n</tool_call>");

        //Assert
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"options\":{\"unit\":\"celsius\"}}");
    }

    /// <summary>A call split across many chunks is still one call, and nothing leaks out as content.</summary>
    [Fact]
    public void AddContent_holds_a_call_back_until_it_is_complete()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "Let me check. ", "<tool", "_call>", "\n<function=get_", "weather>\n<parameter=city",
            ">\nParis\n</param", "eter>\n</function>\n</tool_", "call>");

        //Assert
        transcript.Content.Should().Be("Let me check. ");
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\"}");
    }

    /// <summary>Two calls in one reply are both read.</summary>
    [Fact]
    public void AddContent_reads_two_calls_in_one_reply()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\nParis\n</parameter>\n</function>\n</tool_call>\n"
            + "<tool_call>\n<function=get_weather>\n<parameter=city>\nRome\n</parameter>\n</function>\n</tool_call>");

        //Assert
        transcript.Calls.Should().HaveCount(2);
        transcript.Calls[1].ArgumentsJson.Should().Contain("Rome");
    }

    /// <summary>A model that writes JSON inside the tags anyway is understood.</summary>
    [Fact]
    public void AddContent_reads_a_json_body_inside_the_tags()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>{\"name\": \"get_weather\", \"arguments\": {\"city\": \"Paris\"}}</tool_call>");

        //Assert
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\"}");
    }

    /// <summary>A block naming a tool nobody offered is handed back as text, tags and all.</summary>
    [Fact]
    public void AddContent_hands_back_an_unknown_tool_as_content()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=launch_rocket>\n</function>\n</tool_call>");

        //Assert
        transcript.Calls.Should().BeEmpty();
        transcript.Content.Should().Contain("launch_rocket");
        transcript.Content.Should().StartWith("<tool_call>");
    }

    /// <summary>A reply with no call at all is content from beginning to end.</summary>
    [Fact]
    public void AddContent_passes_a_reply_with_no_call_straight_through()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser, "The weather in Paris ", "is sunny.");

        //Assert
        transcript.Content.Should().Be("The weather in Paris is sunny.");
        transcript.Calls.Should().BeEmpty();
    }

    /// <summary>A call the model never finished is dropped rather than shown half written.</summary>
    [Fact]
    public void Flush_drops_a_call_that_was_never_finished()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser, "here goes ", "<tool_call>\n<function=get_weather>\n");

        //Assert
        transcript.Content.Should().Be("here goes ");
        transcript.Calls.Should().BeEmpty();
    }

    /// <summary>A parameter whose value contains the closing literal keeps the whole of it.</summary>
    [Fact]
    public void AddContent_keeps_a_value_that_contains_the_parameter_closing_literal()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\nsay </parameter> then stop\n"
            + "</parameter>\n</function>\n</tool_call>");

        //Assert
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"say </parameter> then stop\"}");
    }

    /// <summary>The literal still ends a value when the next parameter follows it.</summary>
    [Fact]
    public void AddContent_ends_a_value_at_the_literal_the_next_parameter_follows()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\nParis\n</parameter>\n"
            + "<parameter=days>\n3\n</parameter>\n</function>\n</tool_call>");

        //Assert
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\",\"days\":3}");
    }

    /// <summary>A parameter value containing the block's own closing literal does not end the block.</summary>
    [Fact]
    public void AddContent_keeps_a_value_that_contains_the_block_closing_literal()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\nwrite </tool_call> here\n"
            + "</parameter>\n</function>\n</tool_call>\nafter");

        //Assert
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"write </tool_call> here\"}");
        transcript.Content.Trim().Should().Be("after");
    }

    /// <summary>Two blocks in a row are still two calls, each ending at its own literal.</summary>
    [Fact]
    public void AddContent_reads_two_blocks_in_a_row()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\nParis\n</parameter>\n</function>\n"
            + "</tool_call>\n<tool_call>\n<function=get_weather>\n<parameter=city>\nRome\n</parameter>\n"
            + "</function>\n</tool_call>");

        //Assert
        transcript.Calls.Should().HaveCount(2);
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\"}");
        transcript.Calls[1].ArgumentsJson.Should().Be("{\"city\":\"Rome\"}");
    }

    /// <summary>A block that never closes is handed back as content once it has grown past the cap.</summary>
    [Fact]
    public void AddContent_gives_up_on_a_block_that_grows_past_the_cap()
    {
        //Arrange
        ChatFunctionCallParser parser = NewParser();
        string filler = new string('x', 300_000);

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\n",
            filler, filler, filler, filler, "tail");

        //Assert
        transcript.Calls.Should().BeEmpty();
        transcript.Content.Should().StartWith("<tool_call>");
        transcript.Content.Should().Contain("tail");
        (transcript.Content.Length > 1024 * 1024).Should().BeTrue();
    }

    /// <summary>A tool offered twice under one name is one tool, and the first schema is the one that counts.</summary>
    [Fact]
    public void AddContent_keeps_the_first_of_two_tools_of_the_same_name()
    {
        //Arrange
        List<ToolDefinition> tools = new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Name = "get_weather",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}}}",
            },
            new ToolDefinition
            {
                Name = "get_weather",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"integer\"}}}",
            },
        };

        ChatFunctionCallParser parser =
            new ChatFunctionCallParser(new ToolCallFormat("<tool_call>", "</tool_call>"), tools);

        //Act
        Transcript transcript = Drive(parser,
            "<tool_call>\n<function=get_weather>\n<parameter=city>\n12345\n</parameter>\n"
            + "</function>\n</tool_call>");

        //Assert
        transcript.Calls.Should().HaveCount(1);
        transcript.Calls[0].ArgumentsJson.Should().Be("{\"city\":\"12345\"}");
    }

    private static ChatFunctionCallParser NewParser()
    {
        List<ToolDefinition> tools = new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Name = "get_weather",
                ParametersJsonSchema =
                    "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"},"
                    + "\"days\":{\"type\":\"integer\"},\"options\":{\"type\":\"object\"}}}",
            },
        };

        return new ChatFunctionCallParser(new ToolCallFormat("<tool_call>", "</tool_call>"), tools);
    }

    private static Transcript Drive(ChatFunctionCallParser parser, params string[] chunks)
    {
        StringBuilder content = new StringBuilder();
        List<ToolCall> calls = new List<ToolCall>();

        foreach (string chunk in chunks)
        {
            (IReadOnlyList<ToolCall> found, string text) = parser.AddContent(chunk);
            calls.AddRange(found);
            content.Append(text);
        }

        content.Append(parser.Flush());
        return new Transcript(content.ToString(), calls);
    }

    private sealed class Transcript
    {
        public Transcript(string content, IReadOnlyList<ToolCall> calls)
        {
            Content = content;
            Calls = calls;
        }

        public string Content { get; }

        public IReadOnlyList<ToolCall> Calls { get; }
    }
}
