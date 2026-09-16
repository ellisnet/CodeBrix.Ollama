using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the description a consumer writes a model down in: what a definition insists on, what it
/// fills in, and the options a pull of it is started with.
/// </summary>
public sealed class BundleDefinitionTests
{
    private const string BucketUrl =
        "https://storage.googleapis.com/magentadata/models/music_transformer/primers/fur_elise.mid";

    [Fact]
    public void ForHuggingFace_keeps_the_name_the_repository_and_the_revision()
    {
        //Arrange and act
        BundleDefinition definition = BundleDefinition.ForHuggingFace(
            "hf.co/m-a-p/MuPT-v1-8192-190M:main",
            "m-a-p/MuPT-v1-8192-190M",
            "main",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0"),
            "the 190M parameter model");

        //Assert
        definition.Name.Should().Be("hf.co/m-a-p/MuPT-v1-8192-190M:main");
        definition.Source.Should().Be(PullSource.HuggingFaceFiles);
        definition.Repository.Should().Be("m-a-p/MuPT-v1-8192-190M");
        definition.Revision.Should().Be("main");
        definition.License.LicenseId.Should().Be("apache-2.0");
        definition.Notes.Should().Be("the 190M parameter model");
        definition.Files.Should().BeEmpty();
    }

    [Fact]
    public void ForHuggingFace_without_a_revision_uses_the_default_branch()
        => BundleDefinition.ForHuggingFace("hf.co/skytnt/midi-model", "skytnt/midi-model", null, null, null, null)
            .Revision.Should().Be("main");

    [Fact]
    public void ForHuggingFace_without_a_filter_or_a_licence_still_answers_both()
    {
        //Arrange and act
        BundleDefinition definition = BundleDefinition.ForHuggingFace(
            "hf.co/skytnt/midi-model", "skytnt/midi-model", null, null, null, null);

        //Assert
        definition.Filter.KeepsEverything.Should().BeTrue();
        definition.License.IsStated.Should().BeFalse();
        definition.Notes.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("midi-model")]
    [InlineData("a/b/c")]
    public void ForHuggingFace_without_a_namespace_and_repository_is_refused(string repository)
    {
        //Arrange
        Action act = () => BundleDefinition.ForHuggingFace("hf.co/skytnt/midi-model", repository, null, null, null, null);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForFileList_keeps_the_files()
    {
        //Arrange
        var files = new List<BundleFile> { new BundleFile("primers/fur_elise.mid", BucketUrl, 131) };

        //Act
        BundleDefinition definition = BundleDefinition.ForFileList(
            "storage.googleapis.com/magentadata/music-transformer:unconditional-16", files, null, null, null);

        //Assert
        definition.Source.Should().Be(PullSource.FileList);
        definition.Files.Should().HaveCount(1);
        definition.Files[0].Path.Should().Be("primers/fur_elise.mid");
        definition.Repository.Should().BeNull();
    }

    [Fact]
    public void ForFileList_without_files_is_refused()
    {
        //Arrange
        Action act = () => BundleDefinition.ForFileList(
            "storage.googleapis.com/magentadata/music-transformer:unconditional-16",
            new List<BundleFile>(),
            null,
            null,
            null);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForFileList_copies_the_list_it_was_given()
    {
        //Arrange
        var files = new List<BundleFile> { new BundleFile("primers/fur_elise.mid", BucketUrl, 131) };
        BundleDefinition definition = BundleDefinition.ForFileList("local/magenta/primers:1", files, null, null, null);

        //Act
        files.Add(new BundleFile("primers/clair_de_lune.mid", BucketUrl, 146));

        //Assert
        definition.Files.Should().HaveCount(1);
    }

    [Fact]
    public void ForRegistry_describes_a_model_a_plain_pull_would_fetch()
    {
        //Arrange and act
        BundleDefinition definition = BundleDefinition.ForRegistry("smollm:135m", new LicenseRecord("apache-2.0"), null);

        //Assert
        definition.Source.Should().Be(PullSource.Registry);
        definition.Repository.Should().BeNull();
        definition.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_definition_without_a_name_is_refused(string name)
    {
        //Arrange
        Action act = () => BundleDefinition.ForRegistry(name, null, null);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_definition_whose_name_does_not_parse_is_refused()
    {
        //Arrange
        Action act = () => BundleDefinition.ForRegistry("hf.co/not a model name", null, null);

        //Act and assert
        act.Should().Throw<InvalidModelNameException>();
    }

    [Fact]
    public void ToPullOptions_carries_the_source_the_repository_the_revision_and_the_filter()
    {
        //Arrange
        BundleDefinition definition = BundleDefinition.ForHuggingFace(
            "hf.co/skytnt/midi-model-tv2o-medium:main",
            "skytnt/midi-model-tv2o-medium",
            "0f8f265d4330f4e46527ac2313200254c5757f5f",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0"),
            null);

        //Act
        PullOptions options = definition.ToPullOptions();

        //Assert
        options.Source.Should().Be(PullSource.HuggingFaceFiles);
        options.Repository.Should().Be("skytnt/midi-model-tv2o-medium");
        options.Revision.Should().Be("0f8f265d4330f4e46527ac2313200254c5757f5f");
        options.Filter.Should().Be(FileFilter.ExcludeTrainingArtifacts);
        options.Files.Should().BeEmpty();
        options.RequireHashes.Should().BeFalse();
    }

    [Fact]
    public void ToPullOptions_of_a_file_list_carries_the_files_and_no_revision()
    {
        //Arrange
        var files = new List<BundleFile> { new BundleFile("primers/fur_elise.mid", BucketUrl, 131) };
        BundleDefinition definition = BundleDefinition.ForFileList("local/magenta/primers:1", files, null, null, null);

        //Act
        PullOptions options = definition.ToPullOptions();

        //Assert
        options.Source.Should().Be(PullSource.FileList);
        options.Files.Should().HaveCount(1);
        options.Revision.Should().BeNull();
        options.Repository.Should().BeNull();
    }

    [Fact]
    public void ToString_names_the_bundle_and_where_it_comes_from()
        => BundleDefinition.ForHuggingFace("hf.co/skytnt/midi-model", "skytnt/midi-model", null, null, null, null)
            .ToString().Should().Be("hf.co/skytnt/midi-model (HuggingFaceFiles)");
}
