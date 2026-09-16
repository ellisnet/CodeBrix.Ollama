using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Which of the four ways of asking for constrained output wins, and what grammar each produces.
/// </summary>
public sealed class EngineGrammarTests
{
    private const string Schema = "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}}}";

    /// <summary>A request that asks for nothing is unconstrained.</summary>
    [Fact]
    public void Select_with_nothing_asked_for_is_unconstrained()
    {
        //Act and assert
        EngineGrammar.Select(null).Should().BeNull();
        EngineGrammar.Select(new GenerationOptions()).Should().BeNull();
        EngineGrammar.Select(new GenerationOptions(), null).Should().BeNull();
    }

    /// <summary>An explicit grammar is used as it stands.</summary>
    [Fact]
    public void Select_prefers_an_explicit_grammar()
    {
        //Arrange
        GenerationOptions options = new GenerationOptions
        {
            Grammar = "root ::= \"yes\"",
            JsonSchema = Schema,
            JsonMode = true,
        };

        //Act and assert
        EngineGrammar.Select(options).Should().Be("root ::= \"yes\"");
        EngineGrammar.Select(options, ResponseFormat.Json).Should().Be("root ::= \"yes\"");
    }

    /// <summary>A JSON schema becomes a grammar that only that shape satisfies.</summary>
    [Fact]
    public void Select_turns_a_json_schema_into_a_grammar()
    {
        //Arrange
        GenerationOptions options = new GenerationOptions { JsonSchema = Schema, JsonMode = true };

        //Act
        string grammar = EngineGrammar.Select(options);

        //Assert
        grammar.Should().NotBeNull();
        grammar.Should().Contain("root");
        grammar.Should().Contain("city");
    }

    /// <summary>JSON mode is the any-JSON grammar.</summary>
    [Fact]
    public void Select_turns_json_mode_into_the_any_json_grammar()
        => EngineGrammar.Select(new GenerationOptions { JsonMode = true })
            .Should().Be(JsonSchemaGrammar.JsonGrammar);

    /// <summary>A chat request's response format is the coarsest instruction and comes last.</summary>
    [Fact]
    public void Select_uses_the_response_format_when_the_options_ask_for_nothing()
    {
        //Arrange
        GenerationOptions options = new GenerationOptions();

        //Act and assert
        EngineGrammar.Select(options, ResponseFormat.Json).Should().Be(JsonSchemaGrammar.JsonGrammar);
        EngineGrammar.Select(options, ResponseFormat.FromJsonSchema(Schema)).Should().Contain("city");
        EngineGrammar.Select(null, ResponseFormat.Json).Should().Be(JsonSchemaGrammar.JsonGrammar);
    }

    /// <summary>A schema that is not JSON is a grammar problem, said so.</summary>
    [Fact]
    public void Select_refuses_a_schema_that_is_not_json()
    {
        //Arrange
        Action act = () => EngineGrammar.Select(new GenerationOptions { JsonSchema = "not json" });

        //Assert
        act.Should().Throw<GrammarException>();
    }
}
