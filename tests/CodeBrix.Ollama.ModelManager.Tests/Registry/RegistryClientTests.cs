using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests; //was previously: ollama/ollama server/images_test.go;

/// <summary>
/// Covers the registry client against the in-memory registry: the addresses it builds, the manifest it
/// parses, the bearer challenge it answers, and every error it maps.
/// </summary>
public sealed class RegistryClientTests
{
    private const string RegistryHost = "registry.test";
    private const string Repository = "library/test";

    [Fact]
    public void GetManifestUri_builds_the_v2_manifest_address()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Uri uri = client.GetManifestUri(CreateName());

        //Assert
        uri.ToString().Should().Be("https://registry.test/v2/library/test/manifests/latest");
    }

    [Fact]
    public void GetBlobUri_builds_the_v2_blob_address()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Uri uri = client.GetBlobUri(CreateName(), "sha256:abc");

        //Assert
        uri.ToString().Should().Be("https://registry.test/v2/library/test/blobs/sha256:abc");
    }

    [Fact]
    public void UserAgent_names_the_library_the_operating_system_and_the_architecture()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();

        //Act
        using var client = new RegistryClient(CreateOptions(handler));

        //Assert
        client.UserAgent.Should().StartWith("CodeBrix.Ollama.ModelManager/");
        client.UserAgent.Should().Contain(" (");
        client.UserAgent.Should().Contain("; ");
    }

    [Fact]
    public async Task GetManifestAsync_with_known_model_parses_the_layers()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        handler.AddManifest(Repository, "latest", CreateManifest());
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        RegistryManifestResponse response = await client.GetManifestAsync(
            CreateName(), TestContext.Current.CancellationToken);

        //Assert
        response.Manifest.SchemaVersion.Should().Be(2);
        response.Manifest.Config.MediaType.Should().Be(MediaTypes.Config);
        response.Manifest.Layers.Should().HaveCount(2);
        response.Manifest.Layers[0].MediaType.Should().Be(MediaTypes.Model);
        response.Manifest.Layers[0].Size.Should().Be(4096L);
        response.Manifest.Layers[1].MediaType.Should().Be(MediaTypes.Template);
        response.Manifest.GetTotalSize().Should().Be(4096L + 12L + 7L);
        response.RawBytes.Length.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(
        "{  \"schemaVersion\": 2,  \"mediaType\": \"application/vnd.docker.distribution.manifest.v2+json\",\n"
        + "  \"config\": { \"digest\": \"sha256:abc\", \"mediaType\": \"application/vnd.docker.container.image.v1+json\", \"size\": 50 },\n"
        + "  \"layers\": [{ \"digest\": \"sha256:t1\", \"mediaType\": \"application/vnd.ollama.image.tensor\", \"size\": 1024, \"name\": \"model.weight\" }]\n}")]
    [InlineData(
        "{\"layers\":[{\"size\":999,\"digest\":\"sha256:def\",\"mediaType\":\"application/vnd.ollama.image.model\"}],"
        + "\"schemaVersion\":2,\"config\":{\"size\":50,\"digest\":\"sha256:abc\","
        + "\"mediaType\":\"application/vnd.docker.container.image.v1+json\"},"
        + "\"mediaType\":\"application/vnd.docker.distribution.manifest.v2+json\"}")]
    public async Task GetManifestAsync_keeps_the_raw_bytes_byte_for_byte(string manifestJson)
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] served = Encoding.UTF8.GetBytes(manifestJson);
        handler.AddManifest(Repository, "latest", served);
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        RegistryManifestResponse response = await client.GetManifestAsync(
            CreateName(), TestContext.Current.CancellationToken);

        //Assert
        response.RawBytes.Should().Equal(served);
        FakeRegistryHandler.ComputeDigest(response.RawBytes).Should().Be(FakeRegistryHandler.ComputeDigest(served));
        response.Manifest.SchemaVersion.Should().Be(2);
        response.Manifest.Config.Digest.Should().Be("sha256:abc");
        response.Manifest.Layers.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetManifestAsync_sends_the_manifest_accept_header_and_the_user_agent()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        handler.AddManifest(Repository, "latest", CreateManifest());
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        await client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);

        //Assert
        IReadOnlyList<HttpRequestMessage> requests = handler.Requests;
        requests.Should().HaveCount(1);
        FakeRegistryHandler.GetHeader(requests[0], "Accept").Should().Be(MediaTypes.Manifest);
        // The recorded copy of the header comes back as the product and the comment separately, which is
        // how HttpHeaders parses a User-Agent, so the two values are joined again before comparing.
        string userAgent = FakeRegistryHandler.GetHeader(requests[0], "User-Agent");
        userAgent.Should().StartWith("CodeBrix.Ollama.ModelManager/");
        userAgent.Replace(", ", " ").Should().Be(client.UserAgent);
    }

    [Fact]
    public async Task GetManifestAsync_with_unknown_model_throws_model_not_found()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Func<Task> act = () => client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);

        //Assert
        ModelNotFoundException exception = (await act.Should().ThrowAsync<ModelNotFoundException>()).Which;
        exception.ModelName.Should().Be("registry.test/library/test:latest");
        exception.Message.Should().Contain("not found on registry.test");
    }

    [Fact]
    public async Task GetManifestAsync_with_unauthorized_and_no_token_throws_registry_exception_with_status401()
    {
        //Arrange
        using var handler = new FakeRegistryHandler { RequireAuthorization = true };
        handler.AddManifest(Repository, "latest", CreateManifest());
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Func<Task> act = () => client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        exception.Message.Should().StartWith("unauthorized: access denied");
        exception.Message.Should().Contain("https://auth.test/token");
        exception.ResponseBody.Should().Be("unauthorized");
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetManifestAsync_with_unauthorized_and_a_token_retries_with_the_token()
    {
        //Arrange
        using var handler = new FakeRegistryHandler { RequireAuthorization = true, ExpectedBearerToken = "secret" };
        handler.AddManifest(Repository, "latest", CreateManifest());
        ModelStoreOptions options = CreateOptions(handler);
        options.BearerToken = "secret";
        using var client = new RegistryClient(options);

        //Act
        RegistryManifestResponse response = await client.GetManifestAsync(
            CreateName(), TestContext.Current.CancellationToken);

        //Assert
        response.Manifest.Layers.Should().HaveCount(2);
        IReadOnlyList<HttpRequestMessage> requests = handler.Requests;
        requests.Should().HaveCount(2);
        FakeRegistryHandler.GetHeader(requests[0], "Authorization").Should().BeNull();
        FakeRegistryHandler.GetHeader(requests[1], "Authorization").Should().Be("Bearer secret");
    }

    [Fact]
    public async Task GetManifestAsync_after_a_challenge_sends_the_token_on_later_requests()
    {
        //Arrange
        using var handler = new FakeRegistryHandler { RequireAuthorization = true, ExpectedBearerToken = "secret" };
        handler.AddManifest(Repository, "latest", CreateManifest());
        ModelStoreOptions options = CreateOptions(handler);
        options.BearerToken = "secret";
        using var client = new RegistryClient(options);

        //Act
        await client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);
        await client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);

        //Assert
        handler.Requests.Should().HaveCount(3);
        FakeRegistryHandler.GetHeader(handler.Requests[2], "Authorization").Should().Be("Bearer secret");
    }

    [Fact]
    public async Task GetManifestAsync_with_http_scheme_and_insecure_not_allowed_throws_before_any_request()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        handler.AddManifest(Repository, "latest", CreateManifest());
        using var client = new RegistryClient(CreateOptions(handler));
        ModelName name = ModelName.Parse("http://registry.test/library/test:latest");

        //Act
        Func<Task> act = () => client.GetManifestAsync(name, TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.Message.Should().Be("insecure protocol http");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetManifestAsync_with_http_scheme_allowed_fetches_the_manifest()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        handler.AddManifest(Repository, "latest", CreateManifest());
        ModelStoreOptions options = CreateOptions(handler);
        options.AllowInsecureHttp = true;
        using var client = new RegistryClient(options);
        ModelName name = ModelName.Parse("http://registry.test/library/test:latest");

        //Act
        RegistryManifestResponse response = await client.GetManifestAsync(
            name, TestContext.Current.CancellationToken);

        //Assert
        response.Manifest.Layers.Should().HaveCount(2);
        handler.Requests[0].RequestUri.Scheme.Should().Be("http");
    }

    [Fact]
    public async Task GetManifestAsync_with_server_error_throws_registry_exception_carrying_the_body()
    {
        //Arrange
        using var handler = new FakeRegistryHandler { ManifestStatusCodeOverride = HttpStatusCode.InternalServerError };
        handler.AddManifest(Repository, "latest", CreateManifest());
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Func<Task> act = () => client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        exception.Message.Should().Be("500: the registry is unwell");
        exception.ResponseBody.Should().Be("the registry is unwell");
    }

    [Fact]
    public async Task GetBlobSizeAsync_with_unknown_blob_throws_model_not_found()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Func<Task> act = () => client.GetBlobSizeAsync(
            CreateName(), "sha256:missing", TestContext.Current.CancellationToken);

        //Assert
        ModelNotFoundException exception = (await act.Should().ThrowAsync<ModelNotFoundException>()).Which;
        exception.Message.Should().Contain("not found on registry.test");
    }

    [Fact]
    public async Task GetManifestAsync_with_an_answer_that_is_not_a_manifest_throws_registry_exception()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        handler.AddManifest(Repository, "latest", Encoding.UTF8.GetBytes("{\"error\":\"nope\"}"));
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Func<Task> act = () => client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.Message.Should().Contain("did not answer with a model manifest");
    }

    [Fact]
    public async Task GetManifestAsync_with_an_answer_that_is_not_json_throws_registry_exception()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        handler.AddManifest(Repository, "latest", Encoding.UTF8.GetBytes("<html>not json</html>"));
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        Func<Task> act = () => client.GetManifestAsync(CreateName(), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<RegistryException>();
    }

    [Fact]
    public async Task GetBlobSizeAsync_with_known_blob_returns_the_content_length()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        string digest = handler.AddBlob(new byte[1234]);
        using var client = new RegistryClient(CreateOptions(handler));

        //Act
        long size = await client.GetBlobSizeAsync(CreateName(), digest, TestContext.Current.CancellationToken);

        //Assert
        size.Should().Be(1234L);
        handler.Requests[0].Method.Should().Be(HttpMethod.Head);
    }

    /// <summary>
    /// Builds the options every test here uses: the fake registry, and part sizes and timings small
    /// enough that a download splits and retries within a test run.
    /// </summary>
    /// <param name="handler">The fake registry.</param>
    /// <returns>The options.</returns>
    internal static ModelStoreOptions CreateOptions(FakeRegistryHandler handler)
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

    /// <summary>
    /// The model name the fake registry serves.
    /// </summary>
    /// <returns>The name.</returns>
    internal static ModelName CreateName()
    {
        return ModelName.Parse(RegistryHost + "/" + Repository + ":latest");
    }

    /// <summary>
    /// A manifest with a config layer and two content layers.
    /// </summary>
    /// <returns>The manifest.</returns>
    private static ModelManifest CreateManifest()
    {
        return new ModelManifest
        {
            Config = new ModelLayer(MediaTypes.Config, "sha256:" + new string('c', 64), 12),
            Layers = new List<ModelLayer>
            {
                new ModelLayer(MediaTypes.Model, "sha256:" + new string('a', 64), 4096),
                new ModelLayer(MediaTypes.Template, "sha256:" + new string('b', 64), 7)
            }
        };
    }
}
