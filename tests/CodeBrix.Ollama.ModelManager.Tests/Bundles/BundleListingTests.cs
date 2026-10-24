using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what a listing accepts as its set of files: paths that are unique without regard to case.
/// </summary>
public sealed class BundleListingTests
{
    private const string Url = "https://huggingface.co/skytnt/midi-model/resolve/94f8dc5a/config.json";

    [Fact]
    public void Constructor_keeps_files_whose_paths_differ_by_more_than_case()
    {
        //Arrange
        var files = new[] { new BundleFile("config.json", Url, 2), new BundleFile("weights/config.json", Url, 3) };

        //Act
        var listing = new BundleListing("skytnt/midi-model", null, null, files);

        //Assert
        listing.Files.Should().HaveCount(2);
        listing.TotalBytes.Should().Be(5L);
    }

    [Theory]
    [InlineData("config.json", "config.json")]
    [InlineData("config.json", "Config.json")]
    [InlineData("weights/model.bin", "WEIGHTS/model.bin")]
    public void Constructor_with_paths_that_are_one_file_on_a_case_insensitive_disk_is_refused(string first, string second)
    {
        //Arrange
        var files = new[] { new BundleFile(first, Url), new BundleFile(second, Url) };

        //Act
        Action act = () => new BundleListing(null, null, null, files);

        //Assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain(second);
    }
}
