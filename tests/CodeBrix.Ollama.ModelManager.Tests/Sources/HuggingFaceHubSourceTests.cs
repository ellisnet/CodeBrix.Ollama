using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the Hugging Face listing against the in-memory Hub: what a listing holds, how a revision is
/// pinned to a commit, which hashes come from where, how the filter applies, what is reported about the
/// licence, and how a repository that needs credentials is refused.
/// </summary>
public sealed class HuggingFaceHubSourceTests
{
    private const string Repository = "skytnt/midi-model-tv2o-medium";
    private const string Commit = "0f8f265d4330f4e46527ac2313200254c5757f5f";
    private const string OlderCommit = "94f8dc5af7e744da32913dea85d720c7be148091";

    [Fact]
    public async Task ListAsync_lists_every_file_of_the_commit_the_default_branch_points_at()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.RepositoryId.Should().Be(Repository);
        listing.ResolvedRevision.Should().Be(Commit);
        Paths(listing).Should().Equal(
            "README.md",
            "config.json",
            "model.safetensors",
            "logs/version_0/events.out.tfevents.1727438892",
            "onnx/model_base.onnx");
    }

    [Fact]
    public async Task ListAsync_leaves_out_directory_entries_and_keeps_the_files_inside_them()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        Paths(listing).Should().NotContain("logs");
        Paths(listing).Should().NotContain("onnx");
        Paths(listing).Should().Contain("logs/version_0/events.out.tfevents.1727438892");
    }

    [Fact]
    public async Task ListAsync_applies_the_filter_and_still_keeps_the_readme()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(
            Repository, null, FileFilter.ExcludeTrainingArtifacts, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        Paths(listing).Should().Equal("README.md", "config.json", "model.safetensors", "onnx/model_base.onnx");
    }

    [Fact]
    public async Task ListAsync_reads_the_sha256_of_a_large_file_entry_and_the_git_identifier_of_all_of_them()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        BundleFile weights = Find(listing, "model.safetensors");
        BundleFile config = Find(listing, "config.json");
        weights.Sha256.Should().Be(FakeHubHandler.ComputeDigest(Bytes("weights")).Substring("sha256:".Length));
        weights.GitSha1.Should().NotBeNull();
        config.Sha256.Should().BeNull();
        config.GitSha1.Should().NotBeNull();
    }

    [Fact]
    public async Task ListAsync_states_the_size_of_every_file()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        Find(listing, "model.safetensors").Size.Should().Be(Bytes("weights").Length);
        listing.TotalBytes.Should().Be(listing.Files.Sum(file => file.Size));
    }

    [Fact]
    public async Task ListAsync_pins_the_commit_in_every_address()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, "main", null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        Find(listing, "config.json").Url.AbsoluteUri
            .Should().Be("https://huggingface.co/" + Repository + "/resolve/" + Commit + "/config.json");
        listing.Files.All(file => file.Url.AbsoluteUri.Contains(Commit)).Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_with_the_tag_latest_lists_the_default_branch()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, "latest", null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        source.Revision.Should().Be("main");
        listing.ResolvedRevision.Should().Be(Commit);
        handler.RequestedUrls().Any(url => url.Contains("/revision/")).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_with_a_named_revision_resolves_it_to_a_commit()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.AddRevision(Repository, "v1.1", OlderCommit);
        handler.AddFile(Repository, OlderCommit, "config.json", Bytes("older config"), false);
        using var source = new HuggingFaceHubSource(Repository, "v1.1", null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.ResolvedRevision.Should().Be(OlderCommit);
        Paths(listing).Should().Equal("config.json");
        handler.RequestedUrls().Any(url => url.EndsWith("/revision/v1.1", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_with_a_revision_that_is_already_a_commit_resolves_nothing()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, Commit, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.ResolvedRevision.Should().Be(Commit);
        handler.RequestedUrls().Any(url => url.Contains("/revision/")).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_reports_the_licence_tag_and_where_it_was_read()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.LicenseId.Should().Be("apache-2.0");
        listing.LicenseSource.Should().Be("https://huggingface.co/" + Repository);
    }

    [Fact]
    public async Task ListAsync_reports_the_card_licence_when_there_is_no_tag()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.LicenseTag = null;
        handler.CardLicense = "mit";
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.LicenseId.Should().Be("mit");
    }

    [Fact]
    public async Task ListAsync_reports_a_licence_file_when_the_repository_states_nothing()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.LicenseTag = null;
        handler.CardLicense = null;
        handler.AddFile(Repository, Commit, "LICENSE", Bytes("Apache License, Version 2.0"), false);
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.LicenseId.Should().BeNull();
        listing.LicenseSource.Should().Be("https://huggingface.co/" + Repository + "/resolve/" + Commit + "/LICENSE");
    }

    [Fact]
    public async Task ListAsync_of_a_repository_that_states_nothing_at_all_reports_no_licence()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.LicenseTag = null;
        handler.CardLicense = null;
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.License.IsStated.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_of_a_gated_repository_without_a_token_says_what_to_do_about_it()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.Gated = true;
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        Func<Task> act = () => source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.Message.Should().Contain("gated or private");
        exception.Message.Should().Contain("ModelStoreOptions.BearerToken");
    }

    [Fact]
    public async Task ListAsync_of_a_private_repository_without_a_token_says_what_to_do_about_it()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.Restricted = true;
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        Func<Task> act = () => source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<RegistryException>()).Which.Message.Should().Contain("gated or private");
    }

    [Fact]
    public async Task ListAsync_of_a_repository_hidden_from_anonymous_callers_says_a_token_may_be_the_answer()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.NotFoundForAnonymous = true;
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        Func<Task> act = () => source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.Message.Should().Contain("was not found");
        exception.Message.Should().Contain("access token");
    }

    [Fact]
    public async Task ListAsync_with_a_token_reads_a_gated_repository_and_sends_the_token_to_the_hub()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.Gated = true;
        handler.NotFoundForAnonymous = true;
        handler.RequireAuthorization = true;
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler, "hf-token"));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files.Should().HaveCount(5);
        handler.AnyAuthorizationSentTo("huggingface.co").Should().BeTrue();
        handler.RequestCountForHost("cdn.fake").Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_without_a_token_sends_no_authorization_at_all()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        handler.AnyAuthorizationSentTo("huggingface.co").Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_escapes_the_segments_of_a_path_in_the_address()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.AddFile(Repository, Commit, "logs/version 1/events out.json", Bytes("{}"), false);
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        BundleFile file = Find(listing, "logs/version 1/events out.json");
        file.Url.AbsoluteUri.Should().Be(
            "https://huggingface.co/" + Repository + "/resolve/" + Commit + "/logs/version%201/events%20out.json");
    }

    [Fact]
    public async Task ListAsync_follows_the_paging_of_a_tree_that_arrives_in_pieces()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        handler.TreePageSize = 2;
        using var source = new HuggingFaceHubSource(Repository, null, null, CreateOptions(handler));

        //Act
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        listing.Files.Should().HaveCount(5);
        handler.RequestedUrls().Count(url => url.Contains("/tree/")).Should().Be(4);
    }

    [Fact]
    public async Task ListAsync_of_a_repository_that_is_not_there_says_so()
    {
        //Arrange
        using FakeHubHandler handler = CreateHub();
        using var source = new HuggingFaceHubSource("nobody/nothing", null, null, CreateOptions(handler));

        //Act
        Func<Task> act = () => source.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<RegistryException>()).Which.Message.Should().Contain("was not found");
    }

    [Theory]
    [InlineData(null, "main")]
    [InlineData("", "main")]
    [InlineData("latest", "main")]
    [InlineData("LATEST", "main")]
    [InlineData("main", "main")]
    [InlineData("v1.1", "v1.1")]
    [InlineData("  refs/pr/3  ", "refs/pr/3")]
    public void NormalizeRevision_maps_the_default_tag_to_the_default_branch(string revision, string expected)
        => HuggingFaceHubSource.NormalizeRevision(revision).Should().Be(expected);

    [Theory]
    [InlineData("huggingface.co", true)]
    [InlineData("HUGGINGFACE.CO", true)]
    [InlineData("hf.co", true)]
    [InlineData("cdn-lfs.hf.co", false)]
    [InlineData("cdn.fake", false)]
    public void IsHubHost_names_only_the_hub(string host, bool expected)
        => HuggingFaceHubSource.IsHubHost(host).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("midi-model")]
    [InlineData("a/b/c")]
    public void A_source_without_a_namespace_and_repository_is_refused(string repository)
    {
        //Arrange
        Action act = () => new HuggingFaceHubSource(repository, null, null, null);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildFileUrl_builds_the_resolve_address_of_one_commit()
        => HuggingFaceHubSource.BuildFileUrl("m-a-p/MuPT-v1-8192-190M", "bf8f270d", "pytorch_model.bin")
            .Should().Be("https://huggingface.co/m-a-p/MuPT-v1-8192-190M/resolve/bf8f270d/pytorch_model.bin");

    /// <summary>
    /// A Hub with one repository whose commit holds a readme, a small configuration file, a large-file
    /// weights file, a training log below a directory and an ONNX export below another.
    /// </summary>
    /// <returns>The handler. The caller disposes it.</returns>
    internal static FakeHubHandler CreateHub()
    {
        var handler = new FakeHubHandler { LicenseTag = "apache-2.0", CardLicense = "apache-2.0" };
        handler.AddRepository(Repository, Commit);
        handler.AddFile(Repository, Commit, "README.md", Bytes("# midi model"), false);
        handler.AddFile(Repository, Commit, "config.json", Bytes("{\"model_type\":\"MIDIModel\"}"), false);
        handler.AddFile(Repository, Commit, "model.safetensors", Bytes("weights"), true);
        handler.AddDirectory(Repository, Commit, "logs");
        handler.AddFile(
            Repository, Commit, "logs/version_0/events.out.tfevents.1727438892", Bytes("training log"), true);
        handler.AddDirectory(Repository, Commit, "onnx");
        handler.AddFile(Repository, Commit, "onnx/model_base.onnx", Bytes("onnx graph"), true);
        return handler;
    }

    /// <summary>
    /// The options a test makes its requests with.
    /// </summary>
    /// <param name="handler">The fake Hub.</param>
    /// <returns>The options.</returns>
    internal static ModelStoreOptions CreateOptions(FakeHubHandler handler)
    {
        return CreateOptions(handler, null);
    }

    /// <summary>
    /// The options a test makes its requests with, with an access token.
    /// </summary>
    /// <param name="handler">The fake Hub.</param>
    /// <param name="token">The token, or <see langword="null"/> for an anonymous caller.</param>
    /// <returns>The options.</returns>
    internal static ModelStoreOptions CreateOptions(FakeHubHandler handler, string token)
    {
        return new ModelStoreOptions
        {
            HttpMessageHandler = handler,
            BearerToken = token,
            MinPartSize = 1024,
            MaxPartSize = 4096,
            MaxConcurrentParts = 4,
            StallTimeout = TimeSpan.FromMilliseconds(300),
            ProgressInterval = TimeSpan.FromMilliseconds(20),
            MaxRetries = 3
        };
    }

    /// <summary>
    /// A deterministic body for a fake file.
    /// </summary>
    /// <param name="text">The text the file holds.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// The paths of a listing, in order.
    /// </summary>
    /// <param name="listing">The listing.</param>
    /// <returns>The paths.</returns>
    private static IReadOnlyList<string> Paths(BundleListing listing)
    {
        return listing.Files.Select(file => file.Path).ToArray();
    }

    /// <summary>
    /// One file of a listing, by its path.
    /// </summary>
    /// <param name="listing">The listing.</param>
    /// <param name="path">The path to find.</param>
    /// <returns>The file.</returns>
    private static BundleFile Find(BundleListing listing, string path)
    {
        return listing.Files.First(file => file.Path == path);
    }
}
