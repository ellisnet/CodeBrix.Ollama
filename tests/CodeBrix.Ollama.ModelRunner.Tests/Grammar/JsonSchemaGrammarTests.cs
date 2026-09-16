using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the JSON schema to GBNF conversion against llama.cpp's own expectation table, one fixture pair
/// per case, asserting the grammar text matches exactly.
/// </summary>
public sealed class JsonSchemaGrammarTests
{
    /// <summary>The generated grammar matches the one llama.cpp generates for the same schema.</summary>
    [Theory]
    [InlineData("1-optional-prop")]
    [InlineData("additional-props-implicit")]
    [InlineData("additional-props-true")]
    [InlineData("additional-props")]
    [InlineData("allof-with-enum-schema")]
    [InlineData("allof-with-multiple-enum-schemas")]
    [InlineData("anyof-ref")]
    [InlineData("anyof")]
    [InlineData("array-with-empty-items-and-prefixitems")]
    [InlineData("array-with-empty-items")]
    [InlineData("boolean")]
    [InlineData("conflicting-names")]
    [InlineData("description-only-no-type-treated-as-unconstrained")]
    [InlineData("empty-schema-object")]
    [InlineData("empty-w-o-additional-props")]
    [InlineData("exotic-formats")]
    [InlineData("integer")]
    [InlineData("literal-string-with-escapes")]
    [InlineData("max-1")]
    [InlineData("max-100")]
    [InlineData("max-30")]
    [InlineData("max-5")]
    [InlineData("maxitems-0")]
    [InlineData("maxitems-1")]
    [InlineData("maxitems-2")]
    [InlineData("min-0-max-23")]
    [InlineData("min-0")]
    [InlineData("min-1")]
    [InlineData("min-10-max-10")]
    [InlineData("min-10")]
    [InlineData("min-123-max-42")]
    [InlineData("min-123")]
    [InlineData("min-15-max-300")]
    [InlineData("min-25")]
    [InlineData("min-3")]
    [InlineData("min-5-max-30")]
    [InlineData("min-5")]
    [InlineData("min-9")]
    [InlineData("min-max-items-with-min-max-values-across-zero")]
    [InlineData("min-max-items-with-min-max-values")]
    [InlineData("min-maxitems")]
    [InlineData("minitems")]
    [InlineData("mix-of-allof-anyof-and-ref-similar-to-https-json-schemastore-org-tsconfig-json")]
    [InlineData("n-optional-props")]
    [InlineData("non-string-const")]
    [InlineData("non-string-enum")]
    [InlineData("nullable-string-array")]
    [InlineData("number")]
    [InlineData("optional-additional-props")]
    [InlineData("optional-props-with-common-prefix")]
    [InlineData("optional-props-with-empty-name")]
    [InlineData("optional-props-with-nested-names")]
    [InlineData("regexp-escapes")]
    [InlineData("regexp-quote")]
    [InlineData("regexp-with-nested-non-capturing-groups")]
    [InlineData("regexp-with-non-capturing-group")]
    [InlineData("regexp-with-top-level-alternation")]
    [InlineData("regexp")]
    [InlineData("required-additional-props")]
    [InlineData("required-optional-additional-props")]
    [InlineData("required-optional-props-each-in-original-order")]
    [InlineData("required-props-in-original-order")]
    [InlineData("simple-regexp")]
    [InlineData("string-array")]
    [InlineData("string-const")]
    [InlineData("string-w-max-length")]
    [InlineData("string-w-min-length-1")]
    [InlineData("string-w-min-length-3")]
    [InlineData("string-w-min-max-length")]
    [InlineData("string")]
    [InlineData("top-level-ref")]
    [InlineData("tuple1")]
    [InlineData("tuple2")]
    public void FromSchema_matches_the_llama_cpp_grammar(string fixtureName)
    {
        //Arrange
        string schema = File.ReadAllText(FixturePath(fixtureName + ".schema.json"));
        string expected = File.ReadAllText(FixturePath(fixtureName + ".expected.gbnf"));

        //Act
        string grammar = JsonSchemaGrammar.FromSchema(schema);

        //Assert
        Normalize(grammar).Should().Be(Normalize(expected));
    }

    /// <summary>A schema llama.cpp refuses is refused here too.</summary>
    [Theory]
    [InlineData("invalid-type")]
    [InlineData("unknown-type")]
    public void FromSchema_rejects_a_schema_llama_cpp_rejects(string fixtureName)
    {
        //Arrange
        string schema = File.ReadAllText(FixturePath(fixtureName + ".schema.json"));
        Action act = () => JsonSchemaGrammar.FromSchema(schema);

        //Assert
        act.Should().Throw<GrammarException>();
    }

    /// <summary>The already parsed overload produces the same grammar as the text one.</summary>
    [Fact]
    public void FromSchema_accepts_a_parsed_element()
    {
        //Arrange
        string schema = "{\"type\": \"integer\", \"minimum\": 0}";

        //Act
        string fromText = JsonSchemaGrammar.FromSchema(schema);
        string fromElement;
        using (JsonDocument document = JsonDocument.Parse(schema))
        {
            fromElement = JsonSchemaGrammar.FromSchema(document.RootElement);
        }

        //Assert
        fromElement.Should().Be(fromText);
    }

    /// <summary>A null schema is rejected.</summary>
    [Fact]
    public void FromSchema_with_null_throws()
    {
        //Arrange
        Action act = () => JsonSchemaGrammar.FromSchema((string)null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Text that is not JSON at all is rejected as a grammar problem.</summary>
    [Fact]
    public void FromSchema_with_invalid_json_throws()
    {
        //Arrange
        Action act = () => JsonSchemaGrammar.FromSchema("{not json");

        //Assert
        act.Should().Throw<GrammarException>();
    }

    /// <summary>A remote reference is refused rather than fetched, because conversion does no I/O.</summary>
    [Fact]
    public void FromSchema_refuses_a_remote_reference()
    {
        //Arrange
        Action act = () => JsonSchemaGrammar.FromSchema(
            "{\"$ref\": \"https://example.com/schema.json#/$defs/foo\"}");

        //Assert
        act.Should().Throw<GrammarException>();
    }

    /// <summary>A pattern using lookahead is widened rather than refused, as llama.cpp widens it.</summary>
    [Fact]
    public void FromSchema_widens_a_pattern_it_cannot_express()
    {
        //Act
        string grammar = JsonSchemaGrammar.FromSchema("{\"type\": \"string\", \"pattern\": \"^(?=foo)bar$\"}");

        //Assert
        grammar.Should().Contain("root ::= \"\\\"\" (\"bar\") \"\\\"\"");
    }

    /// <summary>The any-JSON grammar is llama.cpp's own, and starts at a root rule.</summary>
    [Fact]
    public void JsonGrammar_is_the_any_json_grammar()
    {
        //Assert
        JsonSchemaGrammar.JsonGrammar.Should().StartWith("root   ::= object");
        JsonSchemaGrammar.JsonGrammar.Should().Contain("ws ::= | \" \" | \"\\n\" [ \\t]{0,20}");
    }

    /// <summary>A schema member of the wrong JSON kind is refused as a grammar problem, not a cast failure.</summary>
    [Theory]
    [InlineData("{\"$ref\": 3}")]
    [InlineData("{\"$ref\": null}")]
    [InlineData("{\"$ref\": [\"#/$defs/a\"]}")]
    [InlineData("{\"type\": {}}")]
    [InlineData("{\"type\": []}")]
    [InlineData("{\"enum\": 3}")]
    [InlineData("{\"enum\": {\"a\": 1}}")]
    [InlineData("{\"anyOf\": 3}")]
    [InlineData("{\"oneOf\": {}}")]
    [InlineData("{\"type\": \"object\", \"properties\": 3}")]
    [InlineData("{\"type\": \"object\", \"properties\": [\"name\"]}")]
    [InlineData("{\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"required\": \"name\"}")]
    [InlineData("{\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"required\": {\"name\": true}}")]
    [InlineData("{\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"required\": [3]}")]
    [InlineData("{\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"additionalProperties\": 3}")]
    [InlineData("{\"type\": \"array\", \"items\": {\"type\": \"string\"}, \"minItems\": \"3\"}")]
    [InlineData("{\"type\": \"array\", \"items\": {\"type\": \"string\"}, \"minItems\": 2.5}")]
    [InlineData("{\"type\": \"array\", \"items\": {\"type\": \"string\"}, \"maxItems\": \"3\"}")]
    [InlineData("{\"type\": \"string\", \"minLength\": \"3\"}")]
    [InlineData("{\"type\": \"string\", \"maxLength\": [3]}")]
    [InlineData("{\"type\": \"string\", \"pattern\": 3}")]
    [InlineData("{\"type\": \"string\", \"pattern\": null}")]
    [InlineData("{\"type\": \"integer\", \"minimum\": \"x\"}")]
    [InlineData("{\"type\": \"integer\", \"maximum\": {}}")]
    [InlineData("{\"type\": \"integer\", \"exclusiveMinimum\": \"x\"}")]
    [InlineData("{\"allOf\": 3}")]
    [InlineData("{\"allOf\": {\"properties\": {}}}")]
    [InlineData("{\"allOf\": [{\"anyOf\": 3}]}")]
    [InlineData("{\"allOf\": [{\"$ref\": 3}]}")]
    [InlineData("{\"allOf\": [{\"properties\": 3}]}")]
    [InlineData("{\"allOf\": [{\"enum\": 3}]}")]
    public void FromSchema_rejects_a_member_of_the_wrong_kind(string schema)
    {
        //Arrange
        Action act = () => JsonSchemaGrammar.FromSchema(schema);

        //Assert
        act.Should().Throw<GrammarException>();
    }

    /// <summary>A count that does not fit in a 32-bit integer is refused rather than overflowing.</summary>
    [Theory]
    [InlineData("{\"type\": \"string\", \"minLength\": 99999999999999}")]
    [InlineData("{\"type\": \"string\", \"maxLength\": 99999999999999}")]
    [InlineData("{\"type\": \"array\", \"items\": {\"type\": \"string\"}, \"minItems\": 99999999999999}")]
    [InlineData("{\"type\": \"array\", \"items\": {\"type\": \"string\"}, \"maxItems\": 99999999999999}")]
    [InlineData("{\"type\": \"string\", \"pattern\": \"^a{99999999999}$\"}")]
    [InlineData("{\"type\": \"string\", \"pattern\": \"^a{1,99999999999}$\"}")]
    [InlineData("{\"type\": \"integer\", \"minimum\": 1e60}")]
    public void FromSchema_rejects_a_count_that_does_not_fit(string schema)
    {
        //Arrange
        Action act = () => JsonSchemaGrammar.FromSchema(schema);

        //Assert
        act.Should().Throw<GrammarException>();
    }

    /// <summary>An empty list of alternatives or constants is refused rather than making a degenerate rule.</summary>
    [Theory]
    [InlineData("{\"enum\": []}")]
    [InlineData("{\"anyOf\": []}")]
    [InlineData("{\"oneOf\": []}")]
    [InlineData("{\"type\": \"object\", \"properties\": {\"a\": {\"enum\": []}}}")]
    public void FromSchema_rejects_an_empty_alternation(string schema)
    {
        //Arrange
        Action act = () => JsonSchemaGrammar.FromSchema(schema);

        //Assert
        act.Should().Throw<GrammarException>();
    }

    /// <summary>A reference cycle reached through allOf is refused instead of recursing forever.</summary>
    [Theory]
    [InlineData("{\"allOf\": [{\"$ref\": \"#/$defs/a\"}], \"$defs\": {\"a\": {\"$ref\": \"#/$defs/a\"}}}")]
    [InlineData("{\"allOf\": [{\"$ref\": \"#/$defs/a\"}], \"$defs\": "
        + "{\"a\": {\"$ref\": \"#/$defs/b\"}, \"b\": {\"$ref\": \"#/$defs/a\"}}}")]
    [InlineData("{\"allOf\": [{\"anyOf\": [{\"$ref\": \"#/$defs/a\"}]}], \"$defs\": {\"a\": {\"$ref\": \"#/$defs/a\"}}}")]
    public void FromSchema_rejects_a_reference_cycle_in_allof(string schema)
    {
        //Arrange
        Action act = () => JsonSchemaGrammar.FromSchema(schema);

        //Assert
        act.Should().Throw<GrammarException>();
    }

    /// <summary>The overload that reports warnings hands back the widening the converter had to do.</summary>
    [Fact]
    public void FromSchema_reports_the_warnings_it_collected()
    {
        //Arrange
        IReadOnlyList<string> warnings;

        //Act
        string grammar = JsonSchemaGrammar.FromSchema(
            "{\"type\": \"string\", \"pattern\": \"^(?=foo)bar$\"}", out warnings);

        //Assert
        grammar.Should().Contain("root ::= \"\\\"\" (\"bar\") \"\\\"\"");
        warnings.Should().HaveCount(1);
        warnings[0].Should().Contain("pattern");
    }

    /// <summary>A schema the converter expresses exactly reports no warnings.</summary>
    [Fact]
    public void FromSchema_reports_no_warnings_for_an_exact_schema()
    {
        //Arrange
        IReadOnlyList<string> warnings;

        //Act
        string grammar = JsonSchemaGrammar.FromSchema("{\"type\": \"integer\", \"minimum\": 0}", out warnings);

        //Assert
        grammar.Should().Contain("root ::=");
        warnings.Should().BeEmpty();
    }

    /// <summary>The parsed-element overload reports warnings the same way the text one does.</summary>
    [Fact]
    public void FromSchema_reports_warnings_from_a_parsed_element()
    {
        //Arrange
        string schema = "{\"type\": \"string\", \"pattern\": \"^(?=foo)bar$\"}";
        IReadOnlyList<string> warnings;
        string grammar;

        //Act
        using (JsonDocument document = JsonDocument.Parse(schema))
        {
            grammar = JsonSchemaGrammar.FromSchema(document.RootElement, out warnings);
        }

        //Assert
        grammar.Should().Be(JsonSchemaGrammar.FromSchema(schema));
        warnings.Should().HaveCount(1);
    }

    /// <summary>The path of a fixture beside the test assembly.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The full path.</returns>
    private static string FixturePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "Grammar", fileName);
    }

    /// <summary>
    /// Applies the same normalization llama.cpp's own test applies before comparing: outer whitespace is
    /// trimmed and the indentation of each line is removed.
    /// </summary>
    /// <param name="grammar">The grammar text.</param>
    /// <returns>The normalized text.</returns>
    private static string Normalize(string grammar)
    {
        string text = grammar.Replace("\r\n", "\n").Trim(' ', '\n', '\r', '\t');
        return Regex.Replace(text, "(^|\n)[ \t]+", "$1");
    }
}
