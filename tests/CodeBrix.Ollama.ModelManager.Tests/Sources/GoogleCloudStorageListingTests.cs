using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers turning a public storage bucket prefix into bundle files: the XML listing, the prefix that is
/// stripped off each key, the paging of a truncated listing, and the MD5 that arrives in one of the two
/// hash headers an object carries.
/// </summary>
public sealed class GoogleCloudStorageListingTests
{
    private const string Bucket = "magentadata";
    private const string Prefix = "models/music_transformer/";

    [Fact]
    public async Task ListAsync_lists_the_objects_under_the_prefix_with_their_paths_stripped()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        files.Select(file => file.Path).Should().Equal(
            "checkpoints/unconditional_model_16.ckpt.data-00000-of-00001",
            "checkpoints/unconditional_model_16.ckpt.index",
            "checkpoints/unconditional_model_16.ckpt.meta",
            "primers/c_major_scale.mid",
            "primers/fur_elise.mid");
    }

    [Fact]
    public async Task ListAsync_leaves_out_the_placeholder_of_a_folder()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();
        handler.AddFolderPlaceholder(Prefix + "checkpoints/");

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        files.Select(file => file.Path).Should().NotContain("checkpoints/");
        files.Should().HaveCount(5);
    }

    [Fact]
    public async Task ListAsync_reads_the_size_out_of_the_listing()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        Find(files, "primers/fur_elise.mid").Size.Should().Be(Bytes("fur elise").Length);
    }

    [Fact]
    public async Task ListAsync_takes_the_md5_from_the_hash_header_that_carries_it()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        Find(files, "primers/fur_elise.mid").Md5
            .Should().Be(Convert.ToHexStringLower(
                Convert.FromBase64String(FakeBucketHandler.ComputeBase64Md5(Bytes("fur elise")))));
    }

    [Fact]
    public async Task ListAsync_without_hash_headers_states_no_md5()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();
        handler.SendHashHeaders = false;

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        files.All(file => file.Md5 == null).Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_follows_a_truncated_listing_to_its_end()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();
        handler.PageSize = 2;

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        files.Should().HaveCount(5);
        files.Select(file => file.Path).Distinct().Should().HaveCount(5);
        handler.RequestedUrls().Count(url => url.Contains("prefix=")).Should().Be(3);
    }

    [Fact]
    public async Task ListAsync_continues_from_the_last_key_when_the_bucket_names_no_marker()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();
        handler.PageSize = 2;
        handler.SendNextMarker = false;

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        files.Should().HaveCount(5);
    }

    [Fact]
    public async Task ListAsync_builds_the_address_of_every_object()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        Find(files, "primers/fur_elise.mid").Url.AbsoluteUri.Should().Be(
            "https://storage.googleapis.com/magentadata/models/music_transformer/primers/fur_elise.mid");
    }

    [Fact]
    public async Task ListAsync_with_an_empty_prefix_keeps_the_whole_key_as_the_path()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();

        //Act
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            Bucket, string.Empty, handler, TestContext.Current.CancellationToken);

        //Assert
        files[0].Path.Should().Be(Prefix + "checkpoints/unconditional_model_16.ckpt.data-00000-of-00001");
    }

    [Fact]
    public async Task ListAsync_of_a_bucket_that_is_not_there_says_so()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();

        //Act
        Func<Task> act = () => GoogleCloudStorageListing.ListAsync(
            "nosuchbucket", Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<RegistryException>()).Which.Message.Should().Contain("404");
    }

    [Fact]
    public async Task ListAsync_without_a_bucket_is_refused()
    {
        //Arrange
        using FakeBucketHandler handler = CreateBucket();

        //Act
        Func<Task> act = () => GoogleCloudStorageListing.ListAsync(
            string.Empty, Prefix, handler, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void BuildObjectUrl_escapes_the_segments_of_a_key()
        => GoogleCloudStorageListing.BuildObjectUrl("magentadata", "models/music transformer/a b.mid")
            .Should().Be("https://storage.googleapis.com/magentadata/models/music%20transformer/a%20b.mid");

    /// <summary>
    /// A bucket holding the shape of the Magenta music transformer prefix: three checkpoint files and
    /// two primers, in key order.
    /// </summary>
    /// <returns>The handler. The caller disposes it.</returns>
    internal static FakeBucketHandler CreateBucket()
    {
        var handler = new FakeBucketHandler { BucketName = Bucket };
        handler.AddObject(Prefix + "checkpoints/unconditional_model_16.ckpt.data-00000-of-00001", Bytes("checkpoint data"));
        handler.AddObject(Prefix + "checkpoints/unconditional_model_16.ckpt.index", Bytes("index"));
        handler.AddObject(Prefix + "checkpoints/unconditional_model_16.ckpt.meta", Bytes("meta graph"));
        handler.AddObject(Prefix + "primers/c_major_scale.mid", Bytes("c major scale"));
        handler.AddObject(Prefix + "primers/fur_elise.mid", Bytes("fur elise"));
        return handler;
    }

    /// <summary>
    /// A deterministic body for a fake object.
    /// </summary>
    /// <param name="text">The text the object holds.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// One file of a list, by its path.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <param name="path">The path to find.</param>
    /// <returns>The file.</returns>
    private static BundleFile Find(IReadOnlyList<BundleFile> files, string path)
    {
        return files.First(file => file.Path == path);
    }
}
