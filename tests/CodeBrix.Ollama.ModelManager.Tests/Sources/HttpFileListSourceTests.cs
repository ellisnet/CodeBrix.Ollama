using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the listing of a bundle that is nothing but a list of addresses: what it asks a server for,
/// what it leaves alone, and how it copes with a server that will not answer a HEAD.
/// </summary>
public sealed class HttpFileListSourceTests
{
    private const string Bucket = "magentadata";
    private const string Prefix = "models/music_transformer/";

    [Fact]
    public async Task ListAsync_with_every_size_stated_asks_the_servers_nothing()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        var files = new List<BundleFile>
        {
            new BundleFile("primers/fur_elise.mid", ObjectUrl("primers/fur_elise.mid"), 9),
            new BundleFile("primers/c_major_scale.mid", ObjectUrl("primers/c_major_scale.mid"), 13)
        };
        using var source = new HttpFileListSource(files, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files.Should().HaveCount(2);
        listing.TotalBytes.Should().Be(22L);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_asks_a_head_for_the_size_of_a_file_that_states_none()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        var files = new List<BundleFile>
        {
            new BundleFile("primers/fur_elise.mid", ObjectUrl("primers/fur_elise.mid"))
        };
        using var source = new HttpFileListSource(files, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files[0].Size.Should().Be(GoogleCloudStorageListingTests.Bytes("fur elise").Length);
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(System.Net.Http.HttpMethod.Head);
    }

    [Fact]
    public async Task ListAsync_takes_the_md5_the_answer_carries()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        var files = new List<BundleFile>
        {
            new BundleFile("primers/fur_elise.mid", ObjectUrl("primers/fur_elise.mid"))
        };
        using var source = new HttpFileListSource(files, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files[0].Md5.Should().Be(Convert.ToHexStringLower(
            Convert.FromBase64String(FakeBucketHandler.ComputeBase64Md5(GoogleCloudStorageListingTests.Bytes("fur elise")))));
    }

    [Fact]
    public async Task ListAsync_keeps_an_md5_the_caller_already_stated()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        var files = new List<BundleFile>
        {
            new BundleFile(
                "primers/fur_elise.mid",
                ObjectUrl("primers/fur_elise.mid"),
                BundleFile.UnknownSize,
                null,
                "afc0010e22b1c70760696cac123bbf29",
                null)
        };
        using var source = new HttpFileListSource(files, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files[0].Md5.Should().Be("afc0010e22b1c70760696cac123bbf29");
    }

    [Fact]
    public async Task ListAsync_falls_back_to_a_ranged_read_when_a_head_is_refused()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        handler.RefuseHeadRequests = true;
        var files = new List<BundleFile>
        {
            new BundleFile("primers/fur_elise.mid", ObjectUrl("primers/fur_elise.mid"))
        };
        using var source = new HttpFileListSource(files, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files[0].Size.Should().Be(GoogleCloudStorageListingTests.Bytes("fur elise").Length);
        listing.Files[0].Md5.Should().NotBeNull();
        handler.RangeHeaders().Should().Equal("bytes=0-0");
    }

    [Fact]
    public async Task ListAsync_applies_the_filter_before_it_asks_anything()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        var files = new List<BundleFile>
        {
            new BundleFile("primers/fur_elise.mid", ObjectUrl("primers/fur_elise.mid")),
            new BundleFile("checkpoints/unconditional_model_16.ckpt.index", ObjectUrl("checkpoints/unconditional_model_16.ckpt.index"))
        };
        using var source = new HttpFileListSource(files, new FileFilter(new[] { "primers/**" }, null), CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files.Should().HaveCount(1);
        listing.Files[0].Path.Should().Be("primers/fur_elise.mid");
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListAsync_carries_no_repository_and_no_revision()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        var files = new List<BundleFile> { new BundleFile("primers/fur_elise.mid", ObjectUrl("primers/fur_elise.mid"), 9) };
        using var source = new HttpFileListSource(files, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.RepositoryId.Should().BeNull();
        listing.ResolvedRevision.Should().BeNull();
        listing.License.IsStated.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_when_the_server_will_not_say_how_large_a_file_is_fails_with_the_path()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        var files = new List<BundleFile> { new BundleFile("primers/missing.mid", ObjectUrl("primers/missing.mid")) };
        using var source = new HttpFileListSource(files, null, CreateOptions(handler));

        //Act
        Func<Task> act = () => source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<RegistryException>()).Which.Message.Should().Contain("primers/missing.mid");
    }

    [Fact]
    public async Task ListAsync_over_a_bucket_listing_describes_every_object_it_was_given()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        IReadOnlyList<BundleFile> fromBucket = await GoogleCloudStorageListing.ListAsync(
            Bucket, Prefix, handler, TestContext.Current.CancellationToken);
        using var source = new HttpFileListSource(fromBucket, null, CreateOptions(handler));
        int requestsAfterListing = handler.Requests.Count;

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files.Should().HaveCount(5);
        listing.TotalBytes.Should().Be(fromBucket.Sum(file => file.Size));
        handler.Requests.Should().HaveCount(requestsAfterListing);
    }

    [Fact]
    public void A_list_with_a_null_file_is_refused()
    {
        //Arrange
        var files = new List<BundleFile> { null };
        Action act = () => new HttpFileListSource(files, null);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_source_without_a_list_is_refused()
    {
        //Arrange
        Action act = () => new HttpFileListSource(null, null);

        //Act and assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The address of one object of the fake bucket.
    /// </summary>
    /// <param name="path">The path below the prefix.</param>
    /// <returns>The address.</returns>
    private static string ObjectUrl(string path)
    {
        return GoogleCloudStorageListing.BuildObjectUrl(Bucket, Prefix + path);
    }

    /// <summary>
    /// The options a test makes its requests with.
    /// </summary>
    /// <param name="handler">The fake bucket.</param>
    /// <returns>The options.</returns>
    private static ModelStoreOptions CreateOptions(FakeBucketHandler handler)
    {
        return new ModelStoreOptions
        {
            HttpMessageHandler = handler,
            MinPartSize = 1024,
            MaxPartSize = 4096,
            MaxConcurrentParts = 4,
            StallTimeout = TimeSpan.FromMilliseconds(300),
            ProgressInterval = TimeSpan.FromMilliseconds(20),
            MaxRetries = 3
        };
    }
}
