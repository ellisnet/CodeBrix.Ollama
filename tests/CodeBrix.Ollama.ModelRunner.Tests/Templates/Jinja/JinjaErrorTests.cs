using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Tests for the failure paths: syntax errors carry a line and a column, <c>raise_exception</c>
/// surfaces its own message, and the operations Jinja refuses on an undefined value are refused here.
/// </summary>
public sealed class JinjaErrorTests
{
    /// <summary>A syntax error names the line and the column it was found at.</summary>
    [Fact]
    public void Parse_with_a_syntax_error_reports_the_line_and_column()
    {
        //Arrange
        Action act = () => JinjaTemplate.Parse("line one\nline two\n{% if %}");

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("line 3");
        exception.Message.Should().Contain("column 7");
    }

    /// <summary>Every syntax error says so, and says where.</summary>
    /// <param name="source">The broken template.</param>
    [Theory]
    [InlineData("{{ 1 + }}")]
    [InlineData("{{ x ")]
    [InlineData("{% if x %}")]
    [InlineData("{% for x in %}{% endfor %}")]
    [InlineData("{% endif %}")]
    [InlineData("{% include 'other' %}")]
    [InlineData("{# unclosed")]
    [InlineData("{% raw %}body")]
    [InlineData("{{ 'unterminated }}")]
    [InlineData("{{ (1 }}")]
    [InlineData(@"{{ '\U0011FFFF' }}")]
    public void Parse_with_broken_source_throws_a_template_exception(string source)
    {
        //Arrange
        Action act = () => JinjaTemplate.Parse(source);

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("Jinja template syntax error");
    }

    /// <summary>raise_exception fails the render with the message the template passed.</summary>
    [Fact]
    public void Render_with_raise_exception_surfaces_the_message()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{{ raise_exception('No user query found in messages.') }}");
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Be("No user query found in messages.");
    }

    /// <summary>Iterating an undefined value is an error, as it is in Jinja.</summary>
    [Fact]
    public void Render_iterating_an_undefined_value_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{% for x in missing %}{{ x }}{% endfor %}");
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("'missing' is undefined");
    }

    /// <summary>The operations Jinja refuses on an undefined value are refused here too.</summary>
    /// <param name="source">The template.</param>
    [Theory]
    [InlineData("{{ missing + 1 }}")]
    [InlineData("{{ 'a' + missing }}")]
    [InlineData("{{ missing | length }}")]
    [InlineData("{{ missing < 1 }}")]
    [InlineData("{{ missing() }}")]
    [InlineData("{% for x in missing %}{% endfor %}")]
    public void Render_with_an_undefined_value_throws(string source)
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(source);
        Action act = () => template.Render(new Dictionary<string, object>());

        //Assert
        act.Should().Throw<ChatTemplateException>();
    }

    /// <summary>A misused filter fails the render rather than rendering something wrong.</summary>
    /// <param name="source">The template.</param>
    [Theory]
    [InlineData("{{ 'a' | no_such_filter }}")]
    [InlineData("{{ 'a' is no_such_test }}")]
    [InlineData("{{ 'a' | abs }}")]
    [InlineData("{{ 1 / 0 }}")]
    [InlineData("{{ 'a' + 1 }}")]
    [InlineData("{% set x = 1 %}{% set x.y = 2 %}")]
    public void Render_with_a_misused_operation_throws(string source)
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(source);
        Action act = () => template.Render(new Dictionary<string, object>());

        //Assert
        act.Should().Throw<ChatTemplateException>();
    }

    /// <summary>A break outside a loop is reported rather than escaping as an odd exception.</summary>
    [Fact]
    public void Render_with_break_outside_a_loop_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{% break %}");
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("outside a loop");
    }

    /// <summary>Null arguments are rejected.</summary>
    [Fact]
    public void Parse_with_null_source_throws()
    {
        //Arrange
        Action act = () => JinjaTemplate.Parse(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Null variables are rejected.</summary>
    [Fact]
    public void Render_with_null_variables_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("x");
        Action act = () => template.Render(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>The Qwen 3.5 template refuses an empty message list with its own message.</summary>
    [Fact]
    public void Render_qwen35_with_no_messages_raises_the_templates_own_error()
    {
        //Arrange
        JinjaTemplate template = JinjaFixtureLoader.LoadTemplate("qwen3.5-35b-a3b");
        Action act = () => template.Render(new Dictionary<string, object>
        {
            ["messages"] = new List<object>(),
        });

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Be("No messages provided.");
    }

    /// <summary>The Qwen 3.5 template refuses a conversation with no user turn.</summary>
    [Fact]
    public void Render_qwen35_with_no_user_turn_raises_the_templates_own_error()
    {
        //Arrange
        JinjaTemplate template = JinjaFixtureLoader.LoadTemplate("qwen3.5-35b-a3b");
        var message = new Dictionary<string, object> { ["role"] = "assistant", ["content"] = "hi" };
        Action act = () => template.Render(new Dictionary<string, object>
        {
            ["messages"] = new List<object> { message },
        });

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Be("No user query found in messages.");
    }

    /// <summary>A macro that calls itself for ever is stopped by the call-depth cap.</summary>
    [Fact]
    public void Render_with_endless_macro_recursion_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{% macro f() %}{{ f() }}{% endmacro %}{{ f() }}");
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("recursing");
    }

    /// <summary>A break inside a macro cannot reach the caller's loop.</summary>
    /// <param name="source">The template.</param>
    [Theory]
    [InlineData("{% macro m() %}{% break %}{% endmacro %}{% for i in [1, 2] %}{{ m() }}{% endfor %}")]
    [InlineData("{% macro m() %}{% continue %}{% endmacro %}{% for i in [1, 2] %}{{ m() }}{% endfor %}")]
    public void Render_with_a_loop_control_inside_a_macro_throws(string source)
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(source);
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("has no loop to act on");
    }

    /// <summary>A macro that never reads kwargs refuses a keyword argument it does not declare.</summary>
    [Fact]
    public void Render_with_an_unexpected_keyword_argument_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{% macro m(a) %}{{ a }}{% endmacro %}{{ m(1, b=2) }}");
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("takes no keyword argument 'b'");
    }

    /// <summary>range() refuses to build a list nobody could want.</summary>
    [Fact]
    public void Render_with_an_enormous_range_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{{ range(20000000)|length }}");
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("more than");
    }

    /// <summary>tojson gives up on a value nested past its depth cap.</summary>
    [Fact]
    public void Render_with_a_deeply_nested_value_in_tojson_throws()
    {
        //Arrange
        object deep = 1L;
        for (int i = 0; i < 250; i++)
        {
            deep = new List<object> { deep };
        }

        JinjaTemplate template = JinjaTemplate.Parse("{{ x|tojson }}");
        Action act = () => template.Render(new Dictionary<string, object> { ["x"] = deep });

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("levels of nesting");
    }

    /// <summary>The round filter names the three methods it knows instead of guessing.</summary>
    [Fact]
    public void Render_with_an_unknown_round_method_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{{ 1.5|round(0, 'nearest') }}");
        Action act = () => template.Render(new Dictionary<string, object>());

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("'common', 'ceil' or 'floor'");
    }

    /// <summary>A method name no switch arm knows is refused instead of quietly doing something else.</summary>
    /// <param name="name">The method name.</param>
    [Theory]
    [InlineData("nope")]
    [InlineData("removeprefixx")]
    public void Invoke_with_an_unknown_string_method_throws(string name)
    {
        //Arrange
        Action act = () => JinjaMethods.Invoke(new JinjaBoundMethod("abc", name), null, null);

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("unknown method '" + name + "'");
    }

    /// <summary>The list and mapping method tables refuse an unknown name too.</summary>
    [Fact]
    public void Invoke_with_an_unknown_container_method_throws()
    {
        //Arrange
        Action list = () => JinjaMethods.Invoke(new JinjaBoundMethod(new List<object>(), "nope"), null, null);
        Action mapping = () => JinjaMethods.Invoke(
            new JinjaBoundMethod(new Dictionary<string, object>(), "nope"), null, null);

        //Assert
        list.Should().Throw<ChatTemplateException>();
        mapping.Should().Throw<ChatTemplateException>();
    }
}
