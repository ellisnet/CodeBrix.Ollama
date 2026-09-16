using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers which files of a bundle are pulled: the glob syntax, the way includes and excludes combine,
/// the rule that keeps a licence and a readme whatever the patterns say, and the training-artifact
/// filter a caller opts in to.
/// </summary>
public sealed class FileFilterTests
{
    private const string Url = "https://huggingface.co/skytnt/midi-model-tv2o-medium/resolve/0f8f265d/model.safetensors";

    [Fact]
    public void Default_keeps_everything()
    {
        //Arrange
        FileFilter filter = FileFilter.Default;

        //Act and assert
        filter.KeepsEverything.Should().BeTrue();
        filter.ShouldInclude("logs/version_0/events.out.tfevents.1727438892").Should().BeTrue();
        filter.ShouldInclude("optimizer.pt").Should().BeTrue();
    }

    [Theory]
    [InlineData("*.json", "config.json", true)]
    [InlineData("*.json", "nested/config.json", false)]
    [InlineData("**/*.json", "nested/config.json", true)]
    [InlineData("**/*.json", "config.json", true)]
    [InlineData("**", "a/b/c.bin", true)]
    [InlineData("logs/**", "logs/version_0/events", true)]
    [InlineData("logs/**", "logsxyz/events", false)]
    [InlineData("model.?in", "model.bin", true)]
    [InlineData("model.?in", "model.bbin", false)]
    [InlineData("**/*.tfevents*", "logs/version_1/events.out.tfevents.172744.0", true)]
    [InlineData("**/*.tfevents*", "events.out.tfevents.172744.0", true)]
    [InlineData("**/optimizer*.pt", "state/optimizer_0.pt", true)]
    [InlineData("optimizer.pt", "state/optimizer.pt", false)]
    public void ShouldInclude_reads_the_glob_the_usual_way(string glob, string path, bool matches)
        => new FileFilter(new[] { glob }, null).ShouldInclude(path).Should().Be(matches);

    [Fact]
    public void ShouldInclude_with_includes_keeps_only_what_matches_one()
    {
        //Arrange
        var filter = new FileFilter(new[] { "*.safetensors", "*.json" }, null);

        //Act and assert
        filter.ShouldInclude("model.safetensors").Should().BeTrue();
        filter.ShouldInclude("config.json").Should().BeTrue();
        filter.ShouldInclude("pytorch_model.bin").Should().BeFalse();
    }

    [Fact]
    public void ShouldInclude_with_both_lists_needs_an_include_and_no_exclude()
    {
        //Arrange
        var filter = new FileFilter(new[] { "**/*.pt" }, new[] { "**/optimizer*.pt" });

        //Act and assert
        filter.ShouldInclude("attribute2music.pt").Should().BeTrue();
        filter.ShouldInclude("optimizer.pt").Should().BeFalse();
        filter.ShouldInclude("config.json").Should().BeFalse();
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("license")]
    [InlineData("LICENSE.txt")]
    [InlineData("LICENSE.md")]
    [InlineData("README")]
    [InlineData("README.md")]
    [InlineData("docs/README.md")]
    public void ShouldInclude_always_keeps_a_licence_or_a_readme(string path)
        => new FileFilter(new[] { "*.safetensors" }, new[] { "**" }).ShouldInclude(path).Should().BeTrue();

    [Fact]
    public void ShouldInclude_with_an_empty_path_keeps_nothing()
        => FileFilter.Default.ShouldInclude(string.Empty).Should().BeFalse();

    [Fact]
    public void ExcludeTrainingArtifacts_leaves_out_logs_events_and_optimizer_state()
    {
        //Arrange
        FileFilter filter = FileFilter.ExcludeTrainingArtifacts;

        //Act and assert
        filter.ShouldInclude("logs/version_0/events.out.tfevents.1727438892.autodl.1410.0").Should().BeFalse();
        filter.ShouldInclude("runs/events.out.tfevents.1727446966").Should().BeFalse();
        filter.ShouldInclude("optimizer.pt").Should().BeFalse();
        filter.ShouldInclude("checkpoint/optimizer_1.pt").Should().BeFalse();
    }

    [Fact]
    public void ExcludeTrainingArtifacts_keeps_everything_an_export_needs()
    {
        //Arrange
        FileFilter filter = FileFilter.ExcludeTrainingArtifacts;

        //Act and assert
        filter.ShouldInclude("model.safetensors").Should().BeTrue();
        filter.ShouldInclude("pytorch_model.bin").Should().BeTrue();
        filter.ShouldInclude("config.json").Should().BeTrue();
        filter.ShouldInclude("tokenizer_config.json").Should().BeTrue();
        filter.ShouldInclude("onnx/model.onnx").Should().BeTrue();
        filter.ShouldInclude("soundfont.sf2").Should().BeTrue();
        filter.ShouldInclude("README.md").Should().BeTrue();
    }

    [Fact]
    public void Apply_keeps_the_order_of_what_it_keeps()
    {
        //Arrange
        var files = new List<BundleFile>
        {
            new BundleFile("README.md", Url, 1279),
            new BundleFile("logs/version_0/events.out.tfevents.1727438892", Url, 650173),
            new BundleFile("model.safetensors", Url, 467560000),
            new BundleFile("optimizer.pt", Url, 2683207740)
        };

        //Act
        IReadOnlyList<BundleFile> kept = FileFilter.ExcludeTrainingArtifacts.Apply(files);

        //Assert
        kept.Should().HaveCount(2);
        kept[0].Path.Should().Be("README.md");
        kept[1].Path.Should().Be("model.safetensors");
    }

    [Fact]
    public void WithExcludes_adds_to_a_filter_without_changing_it()
    {
        //Arrange
        FileFilter original = FileFilter.ExcludeTrainingArtifacts;

        //Act
        FileFilter narrower = original.WithExcludes("*.bin");

        //Assert
        narrower.ShouldInclude("pytorch_model.bin").Should().BeFalse();
        narrower.ExcludeGlobs.Should().HaveCount(5);
        original.ShouldInclude("pytorch_model.bin").Should().BeTrue();
    }

    [Fact]
    public void WithIncludes_adds_to_a_filter_without_changing_it()
    {
        //Arrange
        var original = new FileFilter(new[] { "*.json" }, null);

        //Act
        FileFilter wider = original.WithIncludes("*.safetensors");

        //Assert
        wider.ShouldInclude("model.safetensors").Should().BeTrue();
        wider.IncludeGlobs.Should().HaveCount(2);
        original.ShouldInclude("model.safetensors").Should().BeFalse();
    }
}
