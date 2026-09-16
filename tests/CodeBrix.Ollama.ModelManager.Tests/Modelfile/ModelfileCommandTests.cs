// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: parser/parser_test.go at commit a43fad18.
using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="ModelfileCommand"/>, ported from the Command.String cases exercised by
/// Ollama's parser tests.
/// </summary>
public sealed class ModelfileCommandTests
{
    /// <summary>The constructor keeps the name and argument text as given.</summary>
    [Fact]
    public void Constructor_KeepsNameAndArgs()
    {
        //Act
        var command = new ModelfileCommand("model", "llama3:latest");

        //Assert
        command.Name.Should().Be("model");
        command.Args.Should().Be("llama3:latest");
    }

    /// <summary>A null name is rejected.</summary>
    [Fact]
    public void Constructor_WithNullName_Throws()
    {
        //Arrange
        Action act = () => new ModelfileCommand(null, "value");

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A null argument is rejected.</summary>
    [Fact]
    public void Constructor_WithNullArgs_Throws()
    {
        //Arrange
        Action act = () => new ModelfileCommand("system", null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Each keyword renders the way Ollama's Command.String renders it.</summary>
    /// <param name="name">The command's internal name.</param>
    /// <param name="args">The command's argument text.</param>
    /// <param name="expected">The expected line.</param>
    [Theory]
    [InlineData("model", "llama3:latest", "FROM llama3:latest")]
    [InlineData("model", "  spaced  ", "FROM   spaced  ")]
    [InlineData("license", "MIT", "LICENSE MIT")]
    [InlineData("template", "template1", "TEMPLATE template1")]
    [InlineData("system", "You are a bot.", "SYSTEM You are a bot.")]
    [InlineData("adapter", "adapter1", "ADAPTER adapter1")]
    [InlineData("draft", "./assistant", "DRAFT ./assistant")]
    [InlineData("renderer", "renderer1", "RENDERER renderer1")]
    [InlineData("parser", "parser1", "PARSER parser1")]
    [InlineData("requires", "0.14.0", "REQUIRES 0.14.0")]
    [InlineData("temperature", "0.5", "PARAMETER temperature 0.5")]
    [InlineData("stop", "### User:", "PARAMETER stop ### User:")]
    [InlineData("message", "user: Hey there!", "MESSAGE user Hey there!")]
    [InlineData("message", "system: ", "MESSAGE system ")]
    [InlineData("message", "system:  ", "MESSAGE system \" \"")]
    [InlineData("message", "assistant", "MESSAGE assistant ")]
    public void ToString_RendersTheLine(string name, string args, string expected)
    {
        //Act
        var line = new ModelfileCommand(name, args).ToString();

        //Assert
        line.Should().Be(expected);
    }

    /// <summary>A value holding a newline is wrapped in one double quote when it has no quote of its own.</summary>
    [Fact]
    public void ToString_WithMultilineValue_UsesSingleQuotes()
    {
        //Act
        var line = new ModelfileCommand("system", "\nThis is a\nmultiline system.\n").ToString();

        //Assert
        line.Should().Be("SYSTEM \"\nThis is a\nmultiline system.\n\"");
    }

    /// <summary>A value holding a newline and a double quote is wrapped in three double quotes.</summary>
    [Fact]
    public void ToString_WithMultilineValueHoldingQuote_UsesTripleQuotes()
    {
        //Act
        var line = new ModelfileCommand("system", "\nSay \"Hello!\".\n").ToString();

        //Assert
        line.Should().Be("SYSTEM \"\"\"\nSay \"Hello!\".\n\"\"\"");
    }

    /// <summary>A MESSAGE argument is split on the first ": " only.</summary>
    [Fact]
    public void ToString_WithMessageHoldingSeparator_SplitsOnTheFirstOne()
    {
        //Act
        var line = new ModelfileCommand("message", "user: a: b").ToString();

        //Assert
        line.Should().Be("MESSAGE user a: b");
    }
}
