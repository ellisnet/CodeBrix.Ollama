using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>Tests for <see cref="JinjaParser"/>: the shape of the tree, and Jinja's precedence.</summary>
public sealed class JinjaParserTests
{
    /// <summary>Text and output tags become text and output nodes, in order.</summary>
    [Fact]
    public void Parse_returns_text_and_output_nodes()
    {
        //Act
        IList<JinjaNode> body = JinjaParser.Parse("a{{ 1 }}b");

        //Assert
        body.Should().HaveCount(3);
        body[0].Should().BeOfType<JinjaTextNode>();
        body[1].Should().BeOfType<JinjaOutputNode>();
        body[2].Should().BeOfType<JinjaTextNode>();
    }

    /// <summary>An if statement keeps every arm and the else body.</summary>
    [Fact]
    public void Parse_with_if_elif_else_keeps_every_arm()
    {
        //Act
        var node = (JinjaIfNode)JinjaParser.Parse("{% if a %}1{% elif b %}2{% elif c %}3{% else %}4{% endif %}")[0];

        //Assert
        node.Branches.Should().HaveCount(3);
        node.ElseBody.Should().HaveCount(1);
    }

    /// <summary>A for statement keeps its targets, its filter and its else body.</summary>
    [Fact]
    public void Parse_with_for_keeps_targets_and_filter()
    {
        //Act
        var node = (JinjaForNode)JinjaParser.Parse(
            "{% for k, v in d.items() if k != 'x' %}a{% else %}b{% endfor %}")[0];

        //Assert
        node.Targets.Should().Equal("k", "v");
        node.Condition.Should().NotBeNull();
        node.ElseBody.Should().HaveCount(1);
    }

    /// <summary>A set statement keeps a dotted target path.</summary>
    [Fact]
    public void Parse_with_a_namespace_target_keeps_the_path()
    {
        //Act
        var node = (JinjaSetNode)JinjaParser.Parse("{% set ns.a = 1 %}")[0];

        //Assert
        node.Targets.Should().HaveCount(1);
        node.Targets[0].Should().Equal("ns", "a");
        node.Value.Should().NotBeNull();
    }

    /// <summary>The block form of set carries a body instead of a value.</summary>
    [Fact]
    public void Parse_with_a_set_block_keeps_the_body_and_the_filter()
    {
        //Act
        var node = (JinjaSetNode)JinjaParser.Parse("{% set x | trim %}hi{% endset %}")[0];

        //Assert
        node.Value.Should().BeNull();
        node.Body.Should().HaveCount(1);
        node.Filter.Should().BeOfType<JinjaFilterExpression>();
    }

    /// <summary>A macro keeps its parameters and their defaults.</summary>
    [Fact]
    public void Parse_with_a_macro_keeps_its_parameters()
    {
        //Act
        var node = (JinjaMacroNode)JinjaParser.Parse("{% macro m(a, b=1) %}x{% endmacro %}")[0];

        //Assert
        node.Name.Should().Be("m");
        node.Parameters.Should().HaveCount(2);
        node.Parameters[0].Value.Should().BeNull();
        node.Parameters[1].Value.Should().NotBeNull();
    }

    /// <summary>Multiplication binds tighter than addition.</summary>
    [Fact]
    public void ParseExpressionText_binds_multiplication_tighter_than_addition()
    {
        //Act
        var node = (JinjaBinaryExpression)JinjaParser.ParseExpressionText("1 + 2 * 3");

        //Assert
        node.Operation.Should().Be(JinjaOperator.Add);
        node.Right.Should().BeOfType<JinjaBinaryExpression>();
    }

    /// <summary>Concatenation binds tighter than addition, as it does in Jinja.</summary>
    [Fact]
    public void ParseExpressionText_binds_concat_tighter_than_addition()
    {
        //Act
        var node = (JinjaBinaryExpression)JinjaParser.ParseExpressionText("1 + 2 ~ 3");

        //Assert
        node.Operation.Should().Be(JinjaOperator.Add);
        ((JinjaBinaryExpression)node.Right).Operation.Should().Be(JinjaOperator.Concat);
    }

    /// <summary>A filter binds tighter than the inline conditional on both arms.</summary>
    [Fact]
    public void ParseExpressionText_binds_filters_tighter_than_the_conditional()
    {
        //Act
        var node = (JinjaConditionalExpression)JinjaParser.ParseExpressionText("a | trim if c else b | upper");

        //Assert
        node.WhenTrue.Should().BeOfType<JinjaFilterExpression>();
        node.WhenFalse.Should().BeOfType<JinjaFilterExpression>();
    }

    /// <summary>A test binds tighter than "not", so "not a is defined" negates the test.</summary>
    [Fact]
    public void ParseExpressionText_binds_a_test_tighter_than_not()
    {
        //Act
        var node = (JinjaUnaryExpression)JinjaParser.ParseExpressionText("not a is defined");

        //Assert
        node.Operation.Should().Be(JinjaOperator.Not);
        node.Operand.Should().BeOfType<JinjaTestExpression>();
    }

    /// <summary>"is not" sets the test's negation flag.</summary>
    [Fact]
    public void ParseExpressionText_reads_is_not_as_a_negated_test()
    {
        //Act
        var node = (JinjaTestExpression)JinjaParser.ParseExpressionText("a is not mapping");

        //Assert
        node.Name.Should().Be("mapping");
        node.Negated.Should().BeTrue();
    }

    /// <summary>A bare test argument is taken, but a following keyword is not.</summary>
    [Fact]
    public void ParseExpressionText_reads_a_bare_test_argument()
    {
        //Act
        var node = (JinjaTestExpression)JinjaParser.ParseExpressionText("a is divisibleby 3");

        //Assert
        node.Arguments.Should().HaveCount(1);
    }

    /// <summary>A test followed by "and" takes no argument from the operator.</summary>
    [Fact]
    public void ParseExpressionText_stops_a_test_argument_at_and()
    {
        //Act
        var node = (JinjaBinaryExpression)JinjaParser.ParseExpressionText("a is defined and b");

        //Assert
        node.Operation.Should().Be(JinjaOperator.And);
        ((JinjaTestExpression)node.Left).Arguments.Should().BeEmpty();
    }

    /// <summary>A slice keeps whichever of its three parts were written.</summary>
    [Fact]
    public void ParseExpressionText_reads_a_three_part_slice()
    {
        //Act
        var node = (JinjaSliceExpression)JinjaParser.ParseExpressionText("a[::-1]");

        //Assert
        node.Start.Should().BeNull();
        node.Stop.Should().BeNull();
        node.Step.Should().NotBeNull();
    }

    /// <summary>A call keeps positional and keyword arguments apart.</summary>
    [Fact]
    public void ParseExpressionText_separates_positional_and_keyword_arguments()
    {
        //Act
        var node = (JinjaCallExpression)JinjaParser.ParseExpressionText("f(1, b=2)");

        //Assert
        node.Arguments.Should().HaveCount(2);
        node.Arguments[0].Name.Should().BeNull();
        node.Arguments[1].Name.Should().Be("b");
    }

    /// <summary>An unknown statement name is a syntax error.</summary>
    [Fact]
    public void Parse_with_an_unknown_statement_throws()
    {
        //Arrange
        System.Action act = () => JinjaParser.Parse("{% include 'x' %}");

        //Assert
        act.Should().Throw<ChatTemplateException>();
    }

    /// <summary>An expression nested past the parser's depth cap is refused, not a stack overflow.</summary>
    [Fact]
    public void ParseExpressionText_with_deeply_nested_parentheses_throws()
    {
        //Arrange
        string source = new string('(', 600) + "1" + new string(')', 600);
        System.Action act = () => JinjaParser.ParseExpressionText(source);

        //Act
        ChatTemplateException exception = act.Should().Throw<ChatTemplateException>().Which;

        //Assert
        exception.Message.Should().Contain("Jinja template syntax error");
    }
}
