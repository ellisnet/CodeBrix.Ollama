using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the naming heuristic the general metadata is derived from - the one that turns
/// <c>organization/Model-7B-Instruct-v0.2</c> into an organization, a base name, a size label, a finetune and a
/// version - and the size label a parameter count is written as when the name carries none.
/// </summary>
public sealed class ModelIdComponentsTests
{
    [Fact]
    public void Parse_takes_a_publisher_identifier_apart()
    {
        //Act
        ModelIdComponents components = ModelIdComponents.Parse("an-organization/Model-7B-Instruct-v0.2",
            7_000_000_000);

        //Assert
        components.Organization.Should().Be("an-organization");
        components.FullName.Should().Be("Model-7B-Instruct-v0.2");
        components.Basename.Should().Be("Model");
        components.SizeLabel.Should().Be("7B");
        components.Finetune.Should().Be("Instruct");
        components.Version.Should().Be("v0.2");
    }

    [Fact]
    public void Parse_reads_a_size_that_is_too_far_from_the_real_one_as_a_finetune()
    {
        //Act
        ModelIdComponents components = ModelIdComponents.Parse("model-8192k-190m", 190_000_000);

        //Assert
        components.SizeLabel.Should().Be("190M");
        components.Finetune.Should().Be("8192k");
        components.Basename.Should().Be("model");
    }

    [Fact]
    public void Parse_gives_up_on_a_base_name_when_the_identifier_says_nothing_else()
    {
        //Act
        ModelIdComponents components = ModelIdComponents.Parse("just-a-name", 1000);

        //Assert
        components.Basename.Should().BeNull();
        components.SizeLabel.Should().BeNull();
        components.FullName.Should().Be("just-a-name");
    }

    [Fact]
    public void Parse_reads_a_sentence_as_a_name_and_nothing_else()
    {
        //Act
        ModelIdComponents components = ModelIdComponents.Parse("A Model With A Name", 1000);

        //Assert
        components.FullName.Should().Be("A Model With A Name");
        components.Organization.Should().BeNull();
        components.Basename.Should().BeNull();
    }

    [Fact]
    public void Parse_reads_the_fixture_directory_names()
    {
        //Act
        ModelIdComponents components = ModelIdComponents.Parse("tinyllama-123k", 123_200);

        //Assert
        components.Basename.Should().Be("tinyllama");
        components.SizeLabel.Should().Be("123K");
        components.Finetune.Should().BeNull();
    }

    [Theory]
    [InlineData("tinyllama-123k", "Tinyllama 123k")]
    [InlineData("mupt-190m", "Mupt 190m")]
    [InlineData("an-organization", "An Organization")]
    [InlineData("Model-7B-Instruct-v0.2", "Model 7B Instruct v0.2")]
    public void IdToTitle_titles_words_but_leaves_versions_and_acronyms_alone(string identifier, string expected)
        => ModelIdComponents.IdToTitle(identifier).Should().Be(expected);

    [Theory]
    [InlineData(123_200L, "123K")]
    [InlineData(124_224L, "124K")]
    [InlineData(102_720L, "103K")]
    [InlineData(115_008L, "115K")]
    [InlineData(190_065_408L, "190M")]
    [InlineData(1_970_000_000L, "2.0B")]
    [InlineData(7_000_000_000L, "7.0B")]
    [InlineData(500L, "0.50K")]
    public void SizeLabelFromParameterCount_writes_the_label_the_engine_writes(long parameters, string expected)
        => ModelIdComponents.SizeLabelFromParameterCount(parameters).Should().Be(expected);

    [Fact]
    public void Parse_answers_nothing_for_no_identifier()
        => ModelIdComponents.Parse(null, 0).Should().BeNull();
}
