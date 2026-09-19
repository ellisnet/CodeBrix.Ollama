using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the model-card front-matter reader: the subset it reads, and the promise that anything outside that
/// subset makes only its own key be skipped rather than failing the read.
/// </summary>
public sealed class ModelCardTests
{
    [Fact]
    public void Parse_reads_a_scalar_a_flow_list_and_a_block_list()
    {
        //Arrange
        string card = "---\nlicense: mit\nlanguage: [en, de]\ntags:\n  - music\n  - art\n---\n\n# Heading\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.TryGetString("license", out string license).Should().BeTrue();
        license.Should().Be("mit");
        result.TryGetStrings("language", out IReadOnlyList<string> languages).Should().BeTrue();
        languages.Should().BeEquivalentTo(new[] { "en", "de" });
        result.TryGetStrings("tags", out IReadOnlyList<string> tags).Should().BeTrue();
        tags.Should().BeEquivalentTo(new[] { "music", "art" });
    }

    [Fact]
    public void Parse_reads_quoted_scalars_and_skips_comments()
    {
        //Arrange
        string card = "---\n# a comment line\nlicense: \"apache-2.0\"  # trailing comment\n"
            + "license_name: 'a name'\n---\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.TryGetString("license", out string license).Should().BeTrue();
        license.Should().Be("apache-2.0");
        result.TryGetString("license_name", out string name).Should().BeTrue();
        name.Should().Be("a name");
    }

    [Fact]
    public void Parse_skips_a_key_whose_value_is_a_nested_mapping()
    {
        //Arrange
        string card = "---\nlicense: mit\nbase_model:\n  name: something\n  revision: main\ntags: [a]\n---\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.TryGetString("base_model", out string _).Should().BeFalse();
        result.TryGetStrings("base_model", out IReadOnlyList<string> _).Should().BeFalse();
        result.TryGetString("license", out string license).Should().BeTrue();
        license.Should().Be("mit");
        result.TryGetStrings("tags", out IReadOnlyList<string> tags).Should().BeTrue();
        tags.Should().BeEquivalentTo(new[] { "a" });
    }

    [Fact]
    public void Parse_skips_a_block_scalar_and_keeps_reading()
    {
        //Arrange
        string card = "---\ndescription: |\n  a long description\n  over two lines\nlicense: mit\n---\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.TryGetString("description", out string _).Should().BeFalse();
        result.TryGetString("license", out string license).Should().BeTrue();
        license.Should().Be("mit");
    }

    [Fact]
    public void Parse_reads_a_tab_indented_block_list_as_if_it_were_spaces()
    {
        //Arrange
        string card = "---\ntags:\n\t- one\n\t- two\n---\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.TryGetStrings("tags", out IReadOnlyList<string> tags).Should().BeTrue();
        tags.Should().BeEquivalentTo(new[] { "one", "two" });
    }

    [Fact]
    public void Parse_returns_nothing_for_a_file_with_no_front_matter()
    {
        //Arrange
        string card = "# Just a heading\n\nSome prose about a model.\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.Keys.Should().BeEmpty();
    }

    [Fact]
    public void Parse_reads_a_scalar_as_a_list_of_one()
    {
        //Arrange
        string card = "---\nlanguage: en\n---\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.TryGetStrings("language", out IReadOnlyList<string> languages).Should().BeTrue();
        languages.Should().BeEquivalentTo(new[] { "en" });
    }

    [Fact]
    public void Parse_stops_at_the_end_of_the_front_matter()
    {
        //Arrange
        string card = "---\nlicense: mit\n---\nlicense: not-this-one\n";

        //Act
        ModelCard result = ModelCard.Parse(card);

        //Assert
        result.TryGetString("license", out string license).Should().BeTrue();
        license.Should().Be("mit");
    }

    [Fact]
    public void Parse_reads_the_fixture_card()
    {
        //Arrange
        string path = System.IO.Path.Combine(
            ConvertFixtureFiles.CheckpointPath("tinyllama-123k"), "README.md");

        //Act
        ModelCard result = ModelCard.Parse(System.IO.File.ReadAllText(path));

        //Assert
        result.TryGetString("license", out string license).Should().BeTrue();
        license.Should().Be("mit");
        result.TryGetStrings("tags", out IReadOnlyList<string> tags).Should().BeTrue();
        tags.Should().BeEquivalentTo(new[] { "test", "fixture" });
        result.TryGetString("pipeline_tag", out string pipeline).Should().BeTrue();
        pipeline.Should().Be("text-generation");
    }
}
