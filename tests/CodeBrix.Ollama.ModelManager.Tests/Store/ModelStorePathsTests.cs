using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="ModelStorePaths"/>: the on-disk shape of a store, which must match what a real
/// Ollama install writes so the two can share a directory.
/// </summary>
public sealed class ModelStorePathsTests
{
    private const string SixtyFourHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string Digest = "sha256:" + SixtyFourHex;

    /// <summary>The store directory is made absolute and the two subdirectories hang off it.</summary>
    [Fact]
    public void Constructor_stores_absolute_paths_for_both_subdirectories()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        var paths = new ModelStorePaths(store.DirectoryPath);

        //Assert
        paths.StoreDirectory.Should().Be(Path.GetFullPath(store.DirectoryPath));
        paths.ManifestsDirectory.Should().Be(Path.Combine(paths.StoreDirectory, "manifests"));
        paths.BlobsDirectory.Should().Be(Path.Combine(paths.StoreDirectory, "blobs"));
    }

    /// <summary>A relative store directory is resolved against the current directory.</summary>
    [Fact]
    public void Constructor_with_relative_directory_makes_it_absolute()
    {
        //Act
        var paths = new ModelStorePaths("relative-store");

        //Assert
        Path.IsPathRooted(paths.StoreDirectory).Should().BeTrue();
        paths.StoreDirectory.Should().Be(Path.GetFullPath("relative-store"));
    }

    /// <summary>An empty store directory is rejected.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_with_empty_directory_throws(string directory)
    {
        //Act
        Action act = () => new ModelStorePaths(directory);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A blob lives in the blobs directory under its digest, with a hyphen for the colon.</summary>
    [Fact]
    public void GetBlobPath_for_valid_digest_returns_hyphenated_name_in_blobs()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        string path = store.Paths.GetBlobPath(Digest);

        //Assert
        path.Should().Be(Path.Combine(store.Paths.BlobsDirectory, "sha256-" + SixtyFourHex));
    }

    /// <summary>A digest already spelled as a file name resolves to the same blob.</summary>
    [Fact]
    public void GetBlobPath_for_file_name_spelling_returns_same_path()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        string fromColon = store.Paths.GetBlobPath(Digest);
        string fromHyphen = store.Paths.GetBlobPath("sha256-" + SixtyFourHex);

        //Assert
        fromHyphen.Should().Be(fromColon);
    }

    /// <summary>Computing a blob path creates nothing on disk.</summary>
    [Fact]
    public void GetBlobPath_does_not_create_directories()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        store.Paths.GetBlobPath(Digest);

        //Assert
        Directory.Exists(store.Paths.BlobsDirectory).Should().BeFalse();
    }

    /// <summary>A malformed digest is rejected with Ollama's own wording.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("sha256:tooshort")]
    public void GetBlobPath_for_invalid_digest_throws(string digest)
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        Action act = () => store.Paths.GetBlobPath(digest);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>The exception message names the problem the way Ollama's ErrInvalidDigestFormat does.</summary>
    [Fact]
    public void GetBlobPath_for_invalid_digest_message_mentions_invalid_digest_format()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ArgumentException caught = null;

        //Act
        try
        {
            store.Paths.GetBlobPath("nonsense");
        }
        catch (ArgumentException exception)
        {
            caught = exception;
        }

        //Assert
        caught.Should().NotBeNull();
        caught.Message.Should().StartWith("invalid digest format");
    }

    /// <summary>A manifest lives four directory levels below the manifests directory.</summary>
    [Fact]
    public void GetManifestPath_for_fully_qualified_name_returns_four_levels()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");

        //Act
        string path = store.Paths.GetManifestPath(name);

        //Assert
        path.Should().Be(Path.Combine(
            store.Paths.ManifestsDirectory,
            "registry.ollama.ai",
            "library",
            "llama3",
            "latest"));
    }

    /// <summary>Every part of the name shows up as its own directory, host and tag included.</summary>
    [Fact]
    public void GetManifestPath_for_explicit_name_uses_every_part()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("example.com/team/model:v1");

        //Act
        string path = store.Paths.GetManifestPath(name);

        //Assert
        path.Should().Be(Path.Combine(
            store.Paths.ManifestsDirectory,
            "example.com",
            "team",
            "model",
            "v1"));
    }

    /// <summary>A name missing a part has no manifest path.</summary>
    [Fact]
    public void GetManifestPath_for_unqualified_name_throws()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        Action act = () => store.Paths.GetManifestPath(default);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Both directories are created and creating them twice is harmless.</summary>
    [Fact]
    public async Task EnsureDirectoriesAsync_creates_manifests_and_blobs()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        await store.Paths.EnsureDirectoriesAsync(TestContext.Current.CancellationToken);
        await store.Paths.EnsureDirectoriesAsync(TestContext.Current.CancellationToken);

        //Assert
        Directory.Exists(store.Paths.ManifestsDirectory).Should().BeTrue();
        Directory.Exists(store.Paths.BlobsDirectory).Should().BeTrue();
    }

    /// <summary>The partial-download sidecars sit beside the blob and carry this library's own suffixes.</summary>
    [Fact]
    public void GetPartialPaths_return_blob_path_with_codebrix_suffixes()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        string blobPath = store.Paths.GetBlobPath(Digest);

        //Act
        string dataPath = store.Paths.GetPartialDataPath(Digest);
        string statePath = store.Paths.GetPartialStatePath(Digest);

        //Assert
        dataPath.Should().Be(blobPath + ".codebrix-partial");
        statePath.Should().Be(blobPath + ".codebrix-parts.json");
    }

    /// <summary>
    /// The sidecar names can never be the ones Ollama uses, which are the blob path plus "-partial"
    /// or "-partial-N", so the two downloaders never write to the same file.
    /// </summary>
    [Fact]
    public void GetPartialPaths_do_not_collide_with_ollama_sidecar_names()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        string blobPath = store.Paths.GetBlobPath(Digest);

        //Act
        string dataPath = store.Paths.GetPartialDataPath(Digest);
        string statePath = store.Paths.GetPartialStatePath(Digest);

        //Assert
        dataPath.Should().NotBe(blobPath + "-partial");
        dataPath.Should().NotBe(blobPath + "-partial-0");
        statePath.Should().NotBe(blobPath + "-partial");
        statePath.Should().NotBe(blobPath + "-partial-0");
    }

    /// <summary>A sidecar name is recognized as this library's leftover; nothing else is.</summary>
    [Theory]
    [InlineData("sha256-" + SixtyFourHex + ".codebrix-partial", true)]
    [InlineData("sha256-" + SixtyFourHex + ".codebrix-parts.json", true)]
    [InlineData("sha256-" + SixtyFourHex + "-partial", false)]
    [InlineData("sha256-" + SixtyFourHex + "-partial-3", false)]
    [InlineData("sha256-" + SixtyFourHex, false)]
    [InlineData("", false)]
    public void IsPartialSidecarFileName_recognizes_only_this_librarys_leftovers(string fileName, bool expected)
    {
        ModelStorePaths.IsPartialSidecarFileName(fileName).Should().Be(expected);
    }
}
