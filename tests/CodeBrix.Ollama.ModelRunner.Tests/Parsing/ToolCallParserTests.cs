using System;
using System.Collections.Generic;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the tool-call parser against every case in Ollama's own tools tests, plus the Qwen 3.5 shape of
/// tagged call the library is expected to handle when the output arrives a few characters at a time.
/// </summary>
public sealed class ToolCallParserTests
{
    private const string QwenTemplate =
        "{{if .ToolCalls}}<tool_call>{{range .ToolCalls}}{\"name\": \"{{.Function.Name}}\", "
        + "\"arguments\": {{.Function.Arguments}}}{{end}}</tool_call>{{end}}";

    private const string DeepSeekTemplate =
        "{{if .ToolCalls}}<|tool▁calls▁begin|>{{range .ToolCalls}}<|tool▁call▁begin|>"
        + "function<|tool▁sep|>get_current_weather\n```json\n{\"location\": \"Tokyo\"}\n```"
        + "<|tool▁call▁end|>{{end}}<|tool▁calls▁end|><|end▁of▁sentence|>{{end}}";

    private const string BareJsonTemplate =
        "{{if .ToolCalls}}{{range .ToolCalls}}{\"name\": \"{{.Function.Name}}\", "
        + "\"arguments\": {{.Function.Arguments}}}{{end}}{{end}}";

    private const string MistralTemplate =
        "{{if .ToolCalls}}[TOOL_CALLS] [{{range .ToolCalls}}{\"name\": \"{{.Function.Name}}\", "
        + "\"arguments\": {{.Function.Arguments}}}{{end}}][/TOOL_CALLS]{{end}}";

    private const string BareListTemplate =
        "{{if .ToolCalls}}[{{range .ToolCalls}}{\"name\": \"{{.Function.Name}}\", "
        + "\"arguments\": {{.Function.Arguments}}}{{end}}]{{end}}";

    /// <summary>The tools every parser test offers the model, matching Ollama's own test set.</summary>
    private static readonly IReadOnlyList<ToolDefinition> Tools = new List<ToolDefinition>
    {
        new ToolDefinition { Name = "get_temperature", Description = "Retrieve the temperature for a given location" },
        new ToolDefinition { Name = "get_conditions", Description = "Retrieve the current weather conditions" },
        new ToolDefinition { Name = "say_hello", Description = "Say hello" },
        new ToolDefinition { Name = "say_hello_world", Description = "Say hello world" },
        new ToolDefinition { Name = "get_address", Description = "Get the address of a given location" },
        new ToolDefinition { Name = "add", Description = "Add two numbers" },
    };

    /// <summary>Output with no tool call at all is content.</summary>
    [Fact]
    public void AddContent_plain_text_is_content()
    {
        AssertParse(QwenTemplate, new[] { "Hello, how can I help you today?" }, "Hello, how can I help you today?");
    }

    /// <summary>An empty chunk produces nothing.</summary>
    [Fact]
    public void AddContent_empty_input()
    {
        AssertParse(QwenTemplate, new[] { "" }, "");
    }

    /// <summary>A single tagged call is parsed and nothing is shown to the user.</summary>
    [Fact]
    public void AddContent_one_tool_call()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "<tool_call>{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"San Francisco\"}}</tool_call>",
            },
            "",
            "get_conditions", "{\"location\":\"San Francisco\"}");
    }

    /// <summary>A call with no arguments is still a call.</summary>
    [Fact]
    public void AddContent_empty_arguments()
    {
        AssertParse(QwenTemplate, new[] { "<tool_call>{\"name\": \"get_conditions\", \"arguments\": {}}</tool_call>" },
            "", "get_conditions", "{}");
    }

    /// <summary>Text before the tag is emitted as content.</summary>
    [Fact]
    public void AddContent_text_before_a_tool_call()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "Let me check the weather. <tool_call>{\"name\": \"get_temperature\", \"arguments\": {\"city\": \"New York\"}}</tool_call>",
            },
            "Let me check the weather. ",
            "get_temperature", "{\"city\":\"New York\"}");
    }

    /// <summary>Naming a tool in prose does not call it.</summary>
    [Fact]
    public void AddContent_a_tool_name_in_prose_is_not_a_call()
    {
        AssertParse(QwenTemplate, new[] { "Let me say hello to the user. I'll use the say_hello tool. " },
            "Let me say hello to the user. I'll use the say_hello tool. ");
    }

    /// <summary>Mistral's bracketed list yields both calls.</summary>
    [Fact]
    public void AddContent_two_calls_in_a_list()
    {
        AssertParse(MistralTemplate,
            new[]
            {
                "[TOOL_CALLS] [{\"name\": \"get_temperature\", \"arguments\": {\"city\": \"London\", \"format\": \"fahrenheit\"}}, {\"name\": \"get_conditions\", \"arguments\": {\"location\": \"Tokyo\"}}][/TOOL_CALLS]",
            },
            "",
            "get_temperature", "{\"city\":\"London\",\"format\":\"fahrenheit\"}",
            "get_conditions", "{\"location\":\"Tokyo\"}");
    }

    /// <summary>Two tagged calls after some text yield two calls.</summary>
    [Fact]
    public void AddContent_two_tagged_calls()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "Okay, let's call both tools! <tool_call>{\"name\": \"get_temperature\", \"arguments\": {\"city\": \"London\", \"format\": \"fahrenheit\"}}</tool_call><tool_call>{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"Tokyo\"}}</tool_call>",
            },
            "Okay, let's call both tools! ",
            "get_temperature", "{\"city\":\"London\",\"format\":\"fahrenheit\"}",
            "get_conditions", "{\"location\":\"Tokyo\"}");
    }

    /// <summary>An argument-less call followed by one with arguments.</summary>
    [Fact]
    public void AddContent_empty_arguments_followed_by_arguments()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "Let me say hello and check the weather. <tool_call>{\"name\": \"say_hello\", \"arguments\": {}}</tool_call><tool_call>{\"name\": \"get_temperature\", \"arguments\": {\"city\": \"London\", \"format\": \"fahrenheit\"}}</tool_call>",
            },
            "Let me say hello and check the weather. ",
            "say_hello", "{}",
            "get_temperature", "{\"city\":\"London\",\"format\":\"fahrenheit\"}");
    }

    /// <summary>The same tool called twice, once without arguments.</summary>
    [Fact]
    public void AddContent_same_tool_twice()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "Let me check the weather. <tool_call>{\"name\": \"get_conditions\", \"arguments\": {}}</tool_call><tool_call>{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"Tokyo\"}}",
            },
            "Let me check the weather. ",
            "get_conditions", "{}",
            "get_conditions", "{\"location\":\"Tokyo\"}");
    }

    /// <summary>DeepSeek's markers separate the reasoning from the call.</summary>
    [Fact]
    public void AddContent_deepseek()
    {
        AssertParse(DeepSeekTemplate,
            new[]
            {
                "<think>Wait, I need to call a tool</think><|tool\u2581calls\u2581begin|><|tool\u2581call\u2581begin|>function<|tool\u2581sep|>get_temperature\n```json\n{\"city\": \"Tokyo\"}\n```<|tool\u2581call\u2581end|><|tool\u2581calls\u2581end|><|end\u2581of\u2581sentence|>",
            },
            "<think>Wait, I need to call a tool</think>",
            "get_temperature", "{\"city\":\"Tokyo\"}");
    }

    /// <summary>The same DeepSeek response cut across its own markers.</summary>
    [Fact]
    public void AddContent_deepseek_in_chunks()
    {
        AssertParse(DeepSeekTemplate,
            new[]
            {
                "<think>Wait",
                ", I need",
                " to call",
                " a tool</think><|too",
                "l\u2581calls\u2581begin",
                "|>",
                "<|tool\u2581call\u2581begin|>function<|tool\u2581sep|>get_temperature\n",
                "```json\n",
                "{\"city\": \"Tokyo\"}\n",
                "```",
                "<|tool\u2581c",
                "all\u2581end|>",
                "<|tool\u2581calls\u2581end|>",
                "<|end\u2581of\u2581sentence|>",
            },
            "<think>Wait, I need to call a tool</think>",
            "get_temperature", "{\"city\":\"Tokyo\"}");
    }

    /// <summary>A model whose template writes bare JSON is parsed from the first brace.</summary>
    [Fact]
    public void AddContent_bare_json()
    {
        AssertParse(BareJsonTemplate, new[] { "{", "\"name\": \"get_temperature\",", "\"arguments\": {", "\"city\": \"Tokyo\"", "}", "}" },
            "", "get_temperature", "{\"city\":\"Tokyo\"}");
    }

    /// <summary>An unfinished bare JSON call produces nothing yet.</summary>
    [Fact]
    public void AddContent_bare_json_still_incomplete()
    {
        AssertParse(BareJsonTemplate, new[] { "{", "\"name\": \"get_temperature\",", "\"arguments\": {" }, "");
    }

    /// <summary>Bare JSON naming a tool that was not offered is content.</summary>
    [Fact]
    public void AddContent_bare_json_naming_an_unknown_tool()
    {
        AssertParse(BareJsonTemplate,
            new[]
            {
                "{",
                "\"name\": \"search\", ",
                "\"arguments\": {",
                "\"query\": \"What is the capital of Canada?\"",
                "}",
                "}",
            },
            "{\"name\": \"search\", \"arguments\": {\"query\": \"What is the capital of Canada?\"}}");
    }

    /// <summary>Once bare JSON turns out not to be a call, the rest is content.</summary>
    [Fact]
    public void AddContent_bare_json_object_then_a_call()
    {
        AssertParse(BareJsonTemplate,
            new[]
            {
                "{\"name\": \"jeff\"}",
                "{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"San Francisco\"}}",
            },
            "{\"name\": \"jeff\"}{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"San Francisco\"}}");
    }

    /// <summary>The same, with the second object split across chunks.</summary>
    [Fact]
    public void AddContent_bare_json_object_then_a_call_split()
    {
        AssertParse(BareJsonTemplate,
            new[]
            {
                "{\"name\": \"jeff\"} {",
                "\"name\": \"get_conditions\", \"arguments\": {\"location\": \"San Francisco\"}}",
            },
            "{\"name\": \"jeff\"} {\"name\": \"get_conditions\", \"arguments\": {\"location\": \"San Francisco\"}}");
    }

    /// <summary>Code that merely contains a brace is content.</summary>
    [Fact]
    public void AddContent_code_with_braces_is_content()
    {
        AssertParse(BareJsonTemplate, new[] { "for { fmt.Println(\"hello\") }" }, "for { fmt.Println(\"hello\") }");
    }

    /// <summary>A bare JSON list yields every call in it.</summary>
    [Fact]
    public void AddContent_bare_list_of_calls()
    {
        AssertParse(BareListTemplate,
            new[]
            {
                "[",
                "{",
                "\"name\": \"get_temperature\", ",
                "\"arguments\": {",
                "\"city\": \"London\"",
                "}",
                "},",
                "{",
                "\"name\": \"get_conditions\", ",
                "\"arguments\": {",
                "\"location\": \"Tokyo\"",
                "}",
                "}]",
            },
            "",
            "get_temperature", "{\"city\":\"London\"}",
            "get_conditions", "{\"location\":\"Tokyo\"}");
    }

    /// <summary>A list whose bracket never closes still yields the call in it.</summary>
    [Fact]
    public void AddContent_bare_list_not_yet_closed()
    {
        AssertParse(BareListTemplate, new[] { "[{", "\"name\": \"get_conditions\", ", "\"arguments\": {", "\"location\": \"Tokyo\"", "}", "}" },
            "", "get_conditions", "{\"location\":\"Tokyo\"}");
    }

    /// <summary>A list naming a tool that was not offered yields nothing.</summary>
    [Fact]
    public void AddContent_bare_list_naming_an_unknown_tool()
    {
        AssertParse(BareListTemplate,
            new[]
            {
                "[",
                "{",
                "\"name\": \"search\", ",
                "\"arguments\": {",
                "\"query\": \"What is the capital of Canada?\"",
                "}",
                "}",
            },
            "");
    }

    /// <summary>Extra closing brackets after a list are ignored.</summary>
    [Fact]
    public void AddContent_bare_list_with_a_trailing_bracket()
    {
        AssertParse(BareListTemplate,
            new[]
            {
                "[",
                "{",
                "\"name\": \"get_conditions\", ",
                "\"arguments\": {",
                "\"location\": \"Tokyo\"",
                "}",
                "}",
                "]",
                "]",
            },
            "",
            "get_conditions", "{\"location\":\"Tokyo\"}");
    }

    /// <summary>Text in brackets is content, not a list of calls.</summary>
    [Fact]
    public void AddContent_bare_list_that_is_not_a_call()
    {
        AssertParse(BareListTemplate, new[] { "[special", " del", "ivery]" }, "[special delivery]");
    }

    /// <summary>A name that is still growing is held back until it settles.</summary>
    [Fact]
    public void AddContent_tool_name_that_is_a_prefix_of_another()
    {
        AssertParse(QwenTemplate, new[] { "<tool_call>", "{", "\"name\": \"say_hello", "_world\",", "\"arguments\": {}}", "}" },
            "", "say_hello_world", "{}");
    }

    /// <summary>The long name then the short name, each called once.</summary>
    [Fact]
    public void AddContent_both_tools_of_a_colliding_pair()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "<tool_call>",
                "{",
                "\"name\": \"say_hello",
                "_world\",",
                "\"arguments\": {}}",
                "</tool_call>",
                "<tool_call>",
                "{",
                "\"name\": \"say_hello",
                "\",",
                "\"arguments\": {}}",
                "</tool_call>",
            },
            "",
            "say_hello_world", "{}",
            "say_hello", "{}");
    }

    /// <summary>A name that could still grow yields nothing.</summary>
    [Fact]
    public void AddContent_ambiguous_name_at_the_end_of_the_stream()
    {
        AssertParse(QwenTemplate, new[] { "<tool_call>{\"name\": \"say_hello" }, "");
    }

    /// <summary>Both colliding names arriving together are matched correctly.</summary>
    [Fact]
    public void AddContent_colliding_names_in_one_chunk()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "<tool_call>{\"name\": \"say_hello\", \"arguments\": {}}</tool_call><tool_call>{\"name\": \"say_hello_world\", \"arguments\": {}}",
            },
            "",
            "say_hello", "{}",
            "say_hello_world", "{}");
    }

    /// <summary>The shorter of two colliding names on its own.</summary>
    [Fact]
    public void AddContent_shorter_of_two_colliding_names()
    {
        AssertParse(QwenTemplate, new[] { "<tool_call>{\"name\": \"say_hello\", \"arguments\": {}}</tool_call>" },
            "", "say_hello", "{}");
    }

    /// <summary>The longer of two colliding names on its own.</summary>
    [Fact]
    public void AddContent_longer_of_two_colliding_names()
    {
        AssertParse(QwenTemplate, new[] { "<tool_call>{\"name\": \"say_hello_world\", \"arguments\": {}}</tool_call>" },
            "", "say_hello_world", "{}");
    }

    /// <summary>A tool name that also appears as an argument key.</summary>
    [Fact]
    public void AddContent_tool_name_that_is_a_substring_of_a_key()
    {
        AssertParse(BareJsonTemplate, new[] { "{", "\"name\": \"get_address\",", "\"arguments\": {", "\"location\": \"London\"", "}", "}" },
            "", "get_address", "{\"location\":\"London\"}");
    }

    /// <summary>The same call, tagged.</summary>
    [Fact]
    public void AddContent_tool_name_that_is_a_substring_of_a_key_tagged()
    {
        AssertParse(QwenTemplate, new[] { "<tool_call>{\"name\": \"get_address\", \"arguments\": {\"location\": \"London\"}}</tool_call>" },
            "", "get_address", "{\"location\":\"London\"}");
    }

    /// <summary>Arguments written before the name are still found.</summary>
    [Fact]
    public void AddContent_arguments_written_before_the_name()
    {
        AssertParse(QwenTemplate, new[] { "<tool_call>{\"arguments\": {\"a\": \"5\", \"b\": \"10\"}, \"name\": \"add\"}</tool_call>" },
            "", "add", "{\"a\":\"5\",\"b\":\"10\"}");
    }

    /// <summary>A Qwen 3.5 style tagged call arriving one character at a time is still one call.</summary>
    [Fact]
    public void AddContent_qwen35_call_one_character_at_a_time()
    {
        //Arrange
        string output = "<tool_call>\n{\"name\": \"get_temperature\", \"arguments\": {\"city\": \"Paris\"}}\n</tool_call>";
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromJinjaTemplateText("<tool_call></tool_call>"), Tools);
        List<ToolCall> calls = new List<ToolCall>();
        StringBuilder content = new StringBuilder();

        //Act
        foreach (char c in output)
        {
            (IReadOnlyList<ToolCall> stepCalls, string stepContent) = parser.AddContent(c.ToString());
            calls.AddRange(stepCalls);
            content.Append(stepContent);
        }

        content.Append(parser.Flush());

        //Assert
        calls.Should().HaveCount(1);
        calls[0].Name.Should().Be("get_temperature");
        calls[0].ArgumentsJson.Should().Be("{\"city\":\"Paris\"}");
        calls[0].Id.Should().BeNull();
        content.ToString().Should().BeEmpty();
    }

    /// <summary>A Qwen 3.5 style call preceded by text emits that text as content.</summary>
    [Fact]
    public void AddContent_qwen35_call_after_text()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "I will look that up. ",
                "<tool_call>\n{\"name\": \"get_temperature\", ",
                "\"arguments\": {\"city\": \"Paris\"}}\n</tool_call>",
            },
            "I will look that up. ",
            "get_temperature", "{\"city\":\"Paris\"}");
    }

    /// <summary>Two Qwen 3.5 style calls in a row yield two calls.</summary>
    [Fact]
    public void AddContent_qwen35_two_calls()
    {
        AssertParse(QwenTemplate,
            new[]
            {
                "<tool_call>\n{\"name\": \"get_temperature\", \"arguments\": {\"city\": \"Paris\"}}\n</tool_call>",
                "<tool_call>\n{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"Paris\"}}\n</tool_call>",
            },
            "",
            "get_temperature", "{\"city\":\"Paris\"}",
            "get_conditions", "{\"location\":\"Paris\"}");
    }

    /// <summary>Malformed JSON after the tag never becomes a call.</summary>
    [Fact]
    public void AddContent_malformed_json_is_not_a_call()
    {
        AssertParse(QwenTemplate,
            new[] { "<tool_call>{name: get_temperature, arguments: {city: Paris}}</tool_call>" }, "");
    }

    /// <summary>A tagged call naming a tool that was not offered yields no call.</summary>
    [Fact]
    public void AddContent_a_tool_that_was_not_offered_is_not_called()
    {
        AssertParse(QwenTemplate,
            new[] { "<tool_call>{\"name\": \"launch_rocket\", \"arguments\": {\"when\": \"now\"}}</tool_call>" },
            "");
    }

    /// <summary>Feeding a response one character at a time gives the same result as feeding it whole.</summary>
    [Theory]
    [InlineData(QwenTemplate, "<tool_call>{\"name\": \"add\", \"arguments\": {\"a\": \"1\", \"b\": \"2\"}}</tool_call>")]
    [InlineData(QwenTemplate, "here you go <tool_call>{\"name\": \"say_hello\", \"arguments\": {}}</tool_call>")]
    [InlineData(QwenTemplate, "no tools needed, the answer is 42")]
    [InlineData(BareJsonTemplate, "{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"Rome\"}}")]
    public void AddContent_one_character_at_a_time_matches_the_whole_response(string template, string output)
    {
        //Arrange
        (string calls, string content) whole = Feed(template, output, output.Length);

        //Act
        (string calls, string content) single = Feed(template, output, 1);

        //Assert
        single.calls.Should().Be(whole.calls);
        single.content.Should().Be(whole.content);
    }

    /// <summary>Random chunk sizes give the same result as feeding the response whole.</summary>
    [Theory]
    [InlineData(QwenTemplate, "<tool_call>{\"name\": \"add\", \"arguments\": {\"a\": \"1\", \"b\": \"2\"}}</tool_call>")]
    [InlineData(QwenTemplate, "here you go <tool_call>{\"name\": \"say_hello\", \"arguments\": {}}</tool_call>")]
    [InlineData(QwenTemplate, "no tools needed, the answer is 42")]
    [InlineData(BareJsonTemplate, "{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"Rome\"}}")]
    public void AddContent_random_chunk_sizes_match_the_whole_response(string template, string output)
    {
        //Arrange
        (string calls, string content) whole = Feed(template, output, output.Length);
        Random random = new Random(20260916);

        //Act and Assert
        for (int attempt = 0; attempt < 25; attempt++)
        {
            (string calls, string content) chunked = FeedRandom(template, output, random);
            chunked.calls.Should().Be(whole.calls);
            chunked.content.Should().Be(whole.content);
        }
    }

    /// <summary>The arguments object is found wherever a model chose to put it.</summary>
    [Theory]
    [InlineData("", "", null)]
    [InlineData("", "   \n\t  ", null)]
    [InlineData("", "{\"format\": \"fahrenheit\", \"location\": \"San Francisco\"", null)]
    [InlineData("", "{\"format\": \"fahrenheit\"}}", "{\"format\":\"fahrenheit\"}")]
    [InlineData("", "{format: fahrenheit, location: \"San Francisco\"}", null)]
    [InlineData("", "{\"name\": \"get_temperature\", \"arguments\": {\"format\": \"fahrenheit\", \"location\": \"San Francisco, CA\"}}", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    [InlineData("", "[tool]get_temperature[args]{\"format\": \"fahrenheit\", \"location\": \"San Francisco, CA\"}[end]", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    [InlineData("", "[{\"name\": \"get_temperature\", \"arguments\": {\"format\": \"fahrenheit\", \"location\": \"San Francisco, CA\"}}", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    [InlineData("", "{\"function\": {\"name\": \"get_temperature\", \"arguments\": {\"format\": \"fahrenheit\", \"location\": \"San Francisco, CA\"}}}", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    [InlineData("", "get_temperature({\"location\": \"San Francisco, CA\"})", "{\"location\":\"San Francisco, CA\"}")]
    [InlineData("", "[{\"name\": \"get_temperature\", \"arguments\": {\"location\": \"San Francisco, CA\", \"format\": \"fahrenheit\"}}, {\"name\": \"get_weather\", \"arguments\": {\"location\": \"San Francisco, CA\", \"format\": \"fahrenheit\"}}]", "{\"location\":\"San Francisco, CA\",\"format\":\"fahrenheit\"}")]
    [InlineData("", "<|tool\u2581calls\u2581begin|><|tool\u2581call\u2581begin|>function<|tool\u2581sep|>get_temperature\n```json\n{\"location\": \"Tokyo\"}\n```<|tool\u2581call\u2581end|><|tool\u2581calls\u2581end|><|end\u2581of\u2581sentence|>", "{\"location\":\"Tokyo\"}")]
    [InlineData("", "\"arguments\": {\"location\": \"Tokyo\"}}</tool_call>", "{\"location\":\"Tokyo\"}")]
    [InlineData("", "{\"name\": \"process_code\", \"arguments\": {\"code\": \"if (x > 0) { return true; }\"}}", "{\"code\":\"if (x > 0) { return true; }\"}")]
    [InlineData("", "{\"name\": \"send_data\", \"arguments\": {\"payload\": \"{\\\"nested\\\": {\\\"key\\\": \\\"value\\\"}}\"}}", "{\"payload\":\"{\\\"nested\\\": {\\\"key\\\": \\\"value\\\"}}\"}")]
    [InlineData("", "{\"name\": \"analyze\", \"arguments\": {\"text\": \"The JSON is: {\\\"key\\\": \\\"val{ue}\\\"}\"}}", "{\"text\":\"The JSON is: {\\\"key\\\": \\\"val{ue}\\\"}\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"query\": \"find } in text\"}} {\"name\": \"other\"}", "{\"query\":\"find } in text\"}")]
    [InlineData("", "{\"name\": \"search\", \"arguments\": {\"pattern\": \"regex: }\"}}", "{\"pattern\":\"regex: }\"}")]
    [InlineData("", "{\"name\": \"analyze\", \"arguments\": {\"data\": \"{\\\"items\\\": [{\\\"value\\\": \\\"}\\\"}, {\\\"code\\\": \\\"if (x) { return y; }\\\"}]}\"}}", "{\"data\":\"{\\\"items\\\": [{\\\"value\\\": \\\"}\\\"}, {\\\"code\\\": \\\"if (x) { return y; }\\\"}]}\"}")]
    [InlineData("", "{\"name\": \"format\", \"arguments\": {\"template\": \"{\\n  \\\"key\\\": \\\"value\\\"\\n}\"}}", "{\"template\":\"{\\n  \\\"key\\\": \\\"value\\\"\\n}\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"text\": \"Unicode: \\u007B and \\u007D\"}}", "{\"text\":\"Unicode: { and }\"}")]
    [InlineData("", "{\"name\": \"batch\", \"arguments\": [\"item1\", \"item2\", \"{\\\"nested\\\": true}\"]}", null)]
    [InlineData("", "{\"name\": \"path\", \"arguments\": {\"dir\": \"C:\\\\Program Files\\\\{App}\\\\\"}}", "{\"dir\":\"C:\\\\Program Files\\\\{App}\\\\\"}")]
    [InlineData("", "{\"name\": \"query\", \"arguments\": {\"sql\": \"SELECT * FROM users WHERE name = '{admin}'\"}}", "{\"sql\":\"SELECT * FROM users WHERE name = '{admin}'\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"data\": \"some {\"", null)]
    [InlineData("", "{\"name\": \"echo\", \"arguments\": {\"msg\": \"He said \\\"Hello {World}\\\" loudly\"}}", "{\"msg\":\"He said \\\"Hello {World}\\\" loudly\"}")]
    [InlineData("", "{\"name\": \"code\", \"arguments\": {\"snippet\": \"// This is a comment with { and }\"}}", "{\"snippet\":\"// This is a comment with { and }\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"path\": \"C:\\\\\\\\{folder}\\\\\\\\\"}}", "{\"path\":\"C:\\\\\\\\{folder}\\\\\\\\\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"a\": \"\", \"b\": \"{value}\"}}", "{\"a\":\"\",\"b\":\"{value}\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"key{\": \"value\", \"key}\": \"value2\"}}", "{\"key{\":\"value\",\"key}\":\"value2\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"code\": \"\\tif (true) {\\n\\t\\treturn;\\n\\t}\"}}", "{\"code\":\"\\tif (true) {\\n\\t\\treturn;\\n\\t}\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"data\": \"before\\u0000{after}\"}}", "{\"data\":\"before\\u0000{after}\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"data\": \"text with quote at end\\\\\\\"\"}}", "{\"data\":\"text with quote at end\\\\\\\"\"}")]
    [InlineData("", "{\"name\": \"test\", \"arguments\": {\"items\": [\"{\", \"}\", {\"key\": \"value\"}]}}", "{\"items\":[\"{\",\"}\",{\"key\":\"value\"}]}")]
    [InlineData("", "{\"name\": \"get_temperature\", \"arguments\": \"{\\\"format\\\": \\\"fahrenheit\\\", \\\"location\\\": \\\"San Francisco, CA\\\"}\"}", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    [InlineData("", "{\"name\": \"get_temperature\", \"parameters\": \"{\\\"format\\\": \\\"fahrenheit\\\", \\\"location\\\": \\\"San Francisco, CA\\\"}\"}", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    [InlineData("get_temperature", "{\"get_temperature\": {\"format\": \"fahrenheit\", \"location\": \"San Francisco, CA\"}}", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    [InlineData("get_temperature", "{\"get_temperature\": \"{\\\"format\\\": \\\"fahrenheit\\\", \\\"location\\\": \\\"San Francisco, CA\\\"}\"}", "{\"format\":\"fahrenheit\",\"location\":\"San Francisco, CA\"}")]
    public void FindArguments_finds_the_arguments_object(string toolName, string buffer, string expected)
    {
        //Act
        int end;
        string arguments = ToolCallParser.FindArguments(toolName, buffer, out end);

        //Assert
        if (expected == null)
        {
            arguments.Should().BeNull();
        }
        else
        {
            arguments.Should().Be(expected);
        }
    }

    /// <summary>A very long argument value peppered with braces is read in one piece.</summary>
    [Fact]
    public void FindArguments_reads_a_very_long_value()
    {
        //Arrange
        StringBuilder value = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            value.Append("a{b}c");
        }

        string buffer = "{\"name\": \"test\", \"arguments\": {\"data\": \"" + value.ToString() + "\"}}";

        //Act
        int end;
        string arguments = ToolCallParser.FindArguments(string.Empty, buffer, out end);

        //Assert
        arguments.Should().Be("{\"data\":\"" + value.ToString() + "\"}");
    }

    /// <summary>The tag is found whole, found partially, or not found at all.</summary>
    [Theory]
    [InlineData("hello world", "<tool_call>", -1, false)]
    [InlineData("<tool_call>", "<tool_call>", 0, true)]
    [InlineData("    <tool_call>\n {\"name\": \"bob\"}", "<tool_call>", 4, true)]
    [InlineData("<tool_call>{\"name\"", "<tool_call>", 0, true)]
    [InlineData("text <tool_call>", "<tool_call>", 5, true)]
    [InlineData("<tool_calls><tool_call>", "<tool_calls>", 0, true)]
    [InlineData("<tool>", "<tool_call>", -1, false)]
    [InlineData("", "<tool_call>", -1, false)]
    [InlineData("test<", "<tool_call>", 4, false)]
    [InlineData("hello <tool_", "<tool_call>", 6, false)]
    [InlineData("calling tools: [", "[", 15, true)]
    [InlineData("{\"name\": \"bob\"", "{", 0, true)]
    [InlineData("\n\n{\n\"name\": \"bob\"", "{", 2, true)]
    public void FindTag_locates_the_tool_call_tag(string buffer, string tag, int expectedIndex,
        bool expectedFound)
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(new ToolCallFormat(tag), Tools) { Buffer = buffer };

        //Act
        bool found;
        int index = parser.FindTag(out found);

        //Assert
        index.Should().Be(expectedIndex);
        found.Should().Be(expectedFound);
    }

    /// <summary>A bare JSON object or array is only complete once its brackets balance.</summary>
    [Theory]
    [InlineData("<tool_call>", "", false)]
    [InlineData("{", "{\"name\": \"get_weather\"", false)]
    [InlineData("{", "{\"name\": \"get_weather\"}", true)]
    [InlineData("{", "{}", true)]
    [InlineData("[", "[{\"name\": \"get_weather\"", false)]
    [InlineData("[", "[{\"name\": \"get_weather\"}]", true)]
    [InlineData("[", "[]", true)]
    [InlineData("{", "{\"note\": \"a}b\"", false)]
    [InlineData("{", "{\"note\": \"a}b\"}", true)]
    [InlineData("[", "[{\"note\": \"x]y\"}", false)]
    public void IsComplete_balances_the_brackets(string tag, string buffer, bool expected)
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(new ToolCallFormat(tag), Tools) { Buffer = buffer };

        //Assert
        parser.IsComplete().Should().Be(expected);
    }

    /// <summary>Buffered text that never became a call is handed back as content at the end.</summary>
    [Theory]
    [InlineData("{", "", 0, "")]
    [InlineData("<tool_call>", "<tool_call>{\"name\": \"get_temperature\"", 0, "<tool_call>{\"name\": \"get_temperature\"")]
    [InlineData("<tool_call>", "<tool_", 0, "<tool_")]
    [InlineData("<tool_call>", "<tool_call>{\"name\": \"launch_rocket\", \"arguments\": {}}", 0, "<tool_call>{\"name\": \"launch_rocket\", \"arguments\": {}}")]
    [InlineData("<tool_call>", "</tool_call>", 1, "")]
    [InlineData("{", "{\"name\": \"get_temperature\"}", 0, "{\"name\": \"get_temperature\"}")]
    [InlineData("{", "{\"hello\": \"world\"}", 0, "{\"hello\": \"world\"}")]
    [InlineData("{", "{\"hello\": \"world\"}", 1, "")]
    [InlineData("[", "[{\"name\": \"get_temperature\"}]", 0, "[{\"name\": \"get_temperature\"}]")]
    [InlineData("{", "{ fmt.Println(\"hello\")", 0, "{ fmt.Println(\"hello\")")]
    public void Flush_returns_the_content_that_was_never_a_call(string tag, string buffer, int callCount,
        string expected)
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(new ToolCallFormat(tag), Tools)
        {
            Buffer = buffer,
            CallCount = callCount,
        };

        //Act
        string content = parser.Flush();

        //Assert
        content.Should().Be(expected);
    }

    /// <summary>A bare JSON call followed by another object ends the parse and yields that object as content.</summary>
    [Fact]
    public void AddContent_bare_json_call_then_another_object()
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromGoTemplateText(BareJsonTemplate), Tools);

        //Act
        (IReadOnlyList<ToolCall> calls, string content) = parser.AddContent(
            "{\"name\": \"get_conditions\", \"arguments\": {\"location\": \"Tokyo\"}}{\"hello\": \"world\"}");

        //Assert
        calls.Should().HaveCount(1);
        calls[0].ArgumentsJson.Should().Be("{\"location\":\"Tokyo\"}");
        content.Should().Be("{\"hello\": \"world\"}");
        parser.State.Should().Be(ToolCallParserState.Done);
    }

    /// <summary>A half-written tag the stream ended on is content rather than lost text.</summary>
    [Fact]
    public void Flush_returns_a_half_written_tag()
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromGoTemplateText(QwenTemplate), Tools);

        //Act
        (IReadOnlyList<ToolCall> calls, string content) = parser.AddContent("Hello <tool_");
        string remainder = parser.Flush();

        //Assert
        calls.Should().BeEmpty();
        content.Should().Be("Hello ");
        remainder.Should().Be("<tool_");
    }

    /// <summary>A tag the model never followed with a usable call is content rather than lost text.</summary>
    [Fact]
    public void Flush_returns_a_tag_that_was_never_a_call()
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromGoTemplateText(QwenTemplate), Tools);
        string output = "<tool_call>{\"name\": \"launch_rocket\", \"arguments\": {\"when\": \"now\"}}</tool_call>";

        //Act
        (IReadOnlyList<ToolCall> calls, string content) = parser.AddContent(output);
        string remainder = parser.Flush();

        //Assert
        calls.Should().BeEmpty();
        content.Should().BeEmpty();
        remainder.Should().Be(output);
    }

    /// <summary>A tag the model never completes stops being held back once the buffer cap is passed.</summary>
    [Fact]
    public void AddContent_gives_up_on_an_endless_tool_call()
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromGoTemplateText(QwenTemplate), Tools);
        string filler = new string('x', 128 * 1024);
        StringBuilder content = new StringBuilder();

        //Act
        content.Append(parser.AddContent("<tool_call>").content);
        for (int i = 0; i < 9; i++)
        {
            content.Append(parser.AddContent(filler).content);
        }

        //Assert
        content.Length.Should().Be("<tool_call>".Length + (9 * 128 * 1024));
        content.ToString().Should().StartWith("<tool_call>xxx");
        parser.State.Should().Be(ToolCallParserState.LookingForTag);
    }

    /// <summary>
    /// Feeds a sequence of chunks through a parser built for a template and checks the content and the
    /// calls it produced.
    /// </summary>
    /// <param name="template">The model's Go chat template.</param>
    /// <param name="inputs">The chunks of model output.</param>
    /// <param name="expectedContent">The content the user should see.</param>
    /// <param name="expectedCalls">The expected calls, as alternating name and compact arguments JSON.</param>
    private static void AssertParse(string template, string[] inputs, string expectedContent,
        params string[] expectedCalls)
    {
        //Arrange
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromGoTemplateText(template), Tools);
        List<ToolCall> calls = new List<ToolCall>();
        StringBuilder content = new StringBuilder();

        //Act
        foreach (string input in inputs)
        {
            (IReadOnlyList<ToolCall> stepCalls, string stepContent) = parser.AddContent(input);
            calls.AddRange(stepCalls);
            content.Append(stepContent);
        }

        //Assert
        content.ToString().Should().Be(expectedContent);
        calls.Should().HaveCount(expectedCalls.Length / 2);
        for (int i = 0; i < calls.Count; i++)
        {
            calls[i].Name.Should().Be(expectedCalls[i * 2]);
            calls[i].ArgumentsJson.Should().Be(expectedCalls[(i * 2) + 1]);
        }
    }

    /// <summary>
    /// Feeds a response through a parser in fixed-size chunks and returns what it produced.
    /// </summary>
    /// <param name="template">The model's Go chat template.</param>
    /// <param name="output">The model's output.</param>
    /// <param name="chunkSize">The chunk size.</param>
    /// <returns>The calls, flattened to text, and the content.</returns>
    private static (string calls, string content) Feed(string template, string output, int chunkSize)
    {
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromGoTemplateText(template), Tools);
        StringBuilder calls = new StringBuilder();
        StringBuilder content = new StringBuilder();

        for (int offset = 0; offset < output.Length; offset += chunkSize)
        {
            string chunk = output.Substring(offset, Math.Min(chunkSize, output.Length - offset));
            (IReadOnlyList<ToolCall> stepCalls, string stepContent) = parser.AddContent(chunk);
            AppendCalls(calls, stepCalls);
            content.Append(stepContent);
        }

        content.Append(parser.Flush());
        return (calls.ToString(), content.ToString());
    }

    /// <summary>
    /// Feeds a response through a parser in randomly sized chunks and returns what it produced.
    /// </summary>
    /// <param name="template">The model's Go chat template.</param>
    /// <param name="output">The model's output.</param>
    /// <param name="random">The chunk size source.</param>
    /// <returns>The calls, flattened to text, and the content.</returns>
    private static (string calls, string content) FeedRandom(string template, string output, Random random)
    {
        ToolCallParser parser = new ToolCallParser(ToolCallFormat.FromGoTemplateText(template), Tools);
        StringBuilder calls = new StringBuilder();
        StringBuilder content = new StringBuilder();

        int offset = 0;
        while (offset < output.Length)
        {
            int size = Math.Min(random.Next(1, 8), output.Length - offset);
            (IReadOnlyList<ToolCall> stepCalls, string stepContent) =
                parser.AddContent(output.Substring(offset, size));
            AppendCalls(calls, stepCalls);
            content.Append(stepContent);
            offset += size;
        }

        content.Append(parser.Flush());
        return (calls.ToString(), content.ToString());
    }

    /// <summary>
    /// Flattens a batch of calls into a comparable string.
    /// </summary>
    /// <param name="builder">The buffer being built.</param>
    /// <param name="calls">The calls.</param>
    private static void AppendCalls(StringBuilder builder, IReadOnlyList<ToolCall> calls)
    {
        foreach (ToolCall call in calls)
        {
            builder.Append(call.Name).Append(' ').Append(call.ArgumentsJson).Append(';');
        }
    }
}
