using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what a pull is asked to do: the defaults an options object starts with and the two properties
/// that answer something usable however they were set.
/// </summary>
public sealed class PullOptionsTests
{
    private const string BucketUrl =
        "https://storage.googleapis.com/magentadata/models/music_transformer/primers/fur_elise.mid";

    [Fact]
    public void A_new_instance_pulls_from_a_registry_and_keeps_everything()
    {
        //Arrange and act
        var options = new PullOptions();

        //Assert
        options.Source.Should().Be(PullSource.Registry);
        options.Filter.KeepsEverything.Should().BeTrue();
        options.Files.Should().BeEmpty();
        options.RequireHashes.Should().BeFalse();
        options.Repository.Should().BeNull();
        options.Revision.Should().BeNull();
    }

    [Fact]
    public void Filter_set_to_nothing_reads_as_the_default_filter()
    {
        //Arrange
        var options = new PullOptions { Filter = FileFilter.ExcludeTrainingArtifacts };

        //Act
        options.Filter = null;

        //Assert
        options.Filter.Should().Be(FileFilter.Default);
    }

    [Fact]
    public void Files_set_to_nothing_reads_as_an_empty_list()
    {
        //Arrange
        var options = new PullOptions
        {
            Files = new List<BundleFile> { new BundleFile("primers/fur_elise.mid", BucketUrl, 131) }
        };

        //Act
        options.Files = null;

        //Assert
        options.Files.Should().BeEmpty();
    }

    [Fact]
    public void ForHuggingFace_names_the_repository_and_the_revision()
    {
        //Arrange and act
        PullOptions options = PullOptions.ForHuggingFace(
            "m-a-p/MuPT-v1-8192-190M", "main", FileFilter.ExcludeTrainingArtifacts);

        //Assert
        options.Source.Should().Be(PullSource.HuggingFaceFiles);
        options.Repository.Should().Be("m-a-p/MuPT-v1-8192-190M");
        options.Revision.Should().Be("main");
        options.Filter.Should().Be(FileFilter.ExcludeTrainingArtifacts);
    }

    [Fact]
    public void ForFileList_names_the_files()
    {
        //Arrange
        var files = new List<BundleFile> { new BundleFile("primers/fur_elise.mid", BucketUrl, 131) };

        //Act
        PullOptions options = PullOptions.ForFileList(files, null);

        //Assert
        options.Source.Should().Be(PullSource.FileList);
        options.Files.Should().HaveCount(1);
        options.Filter.KeepsEverything.Should().BeTrue();
    }

    [Fact]
    public void ForRegistry_is_what_a_pull_without_options_does()
        => PullOptions.ForRegistry().Source.Should().Be(PullSource.Registry);

    [Fact]
    public void DefaultRevision_is_the_default_branch()
        => PullOptions.DefaultRevision.Should().Be("main");
}
