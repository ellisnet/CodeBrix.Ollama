using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="LayerFactory"/>: content addressed blobs, reuse of a blob that is already
/// there, imports that leave the caller's file alone, and the corruption check a pull depends on.
/// </summary>
public sealed class LayerFactoryTests
{
    private const string HelloWorld = "hello world";

    private const string HelloWorldDigest = "sha256:b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";

    private const string AbsentDigest = "sha256:3333333333333333333333333333333333333333333333333333333333333333";

    /// <summary>Streamed content lands in the blobs directory under its own digest.</summary>
    [Fact]
    public async Task CreateFromStreamAsync_produces_digest_size_and_blob_file()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(HelloWorld));

        //Act
        ModelLayer layer = await LayerFactory.CreateFromStreamAsync(
            store.Paths,
            content,
            MediaTypes.Model,
            TestContext.Current.CancellationToken);

        //Assert
        layer.Digest.Should().Be(HelloWorldDigest);
        layer.Size.Should().Be(11);
        layer.MediaType.Should().Be(MediaTypes.Model);
        File.Exists(store.Paths.GetBlobPath(layer.Digest)).Should().BeTrue();
    }

    /// <summary>An empty stream is a valid layer with the empty digest.</summary>
    [Fact]
    public async Task CreateFromStreamAsync_for_empty_content_produces_empty_digest()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await using var content = new MemoryStream(Array.Empty<byte>());

        //Act
        ModelLayer layer = await LayerFactory.CreateFromStreamAsync(
            store.Paths,
            content,
            MediaTypes.License,
            TestContext.Current.CancellationToken);

        //Assert
        layer.Size.Should().Be(0);
        layer.Digest.Should().Be("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    /// <summary>Storing the same bytes twice writes one file and leaves no temporary file behind.</summary>
    [Fact]
    public async Task CreateFromStreamAsync_for_identical_content_reuses_the_existing_blob()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await using var first = new MemoryStream(Encoding.UTF8.GetBytes(HelloWorld));
        await using var second = new MemoryStream(Encoding.UTF8.GetBytes(HelloWorld));

        //Act
        ModelLayer firstLayer = await LayerFactory.CreateFromStreamAsync(
            store.Paths,
            first,
            MediaTypes.Model,
            TestContext.Current.CancellationToken);
        ModelLayer secondLayer = await LayerFactory.CreateFromStreamAsync(
            store.Paths,
            second,
            MediaTypes.Model,
            TestContext.Current.CancellationToken);

        //Assert
        secondLayer.Digest.Should().Be(firstLayer.Digest);
        secondLayer.Size.Should().Be(firstLayer.Size);
        Directory.GetFiles(store.Paths.BlobsDirectory).Should().HaveCount(1);
    }

    /// <summary>Text is stored as UTF-8 with no byte order mark.</summary>
    [Fact]
    public async Task CreateFromTextAsync_writes_utf8_without_byte_order_mark()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        ModelLayer layer = await LayerFactory.CreateFromTextAsync(
            store.Paths,
            HelloWorld,
            MediaTypes.System,
            TestContext.Current.CancellationToken);

        //Assert
        layer.Digest.Should().Be(HelloWorldDigest);
        layer.MediaType.Should().Be(MediaTypes.System);
        byte[] bytes = await File.ReadAllBytesAsync(
            store.Paths.GetBlobPath(layer.Digest),
            TestContext.Current.CancellationToken);
        bytes.Should().Equal(Encoding.UTF8.GetBytes(HelloWorld));
    }

    /// <summary>Bytes handed in directly produce the same blob as the stream overload.</summary>
    [Fact]
    public async Task CreateFromBytesAsync_produces_the_same_blob_as_the_stream_overload()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        ModelLayer layer = await LayerFactory.CreateFromBytesAsync(
            store.Paths,
            Encoding.UTF8.GetBytes(HelloWorld),
            MediaTypes.Template,
            TestContext.Current.CancellationToken);

        //Assert
        layer.Digest.Should().Be(HelloWorldDigest);
        layer.MediaType.Should().Be(MediaTypes.Template);
    }

    /// <summary>Importing a file copies it into the store and leaves the original where it was.</summary>
    [Fact]
    public async Task CreateFromFileAsync_copies_the_file_and_leaves_the_source_in_place()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        string sourcePath = Path.Combine(store.DirectoryPath, "weights.gguf");
        await File.WriteAllTextAsync(sourcePath, HelloWorld, TestContext.Current.CancellationToken);

        //Act
        ModelLayer layer = await LayerFactory.CreateFromFileAsync(
            store.Paths,
            sourcePath,
            MediaTypes.Model,
            "weights.gguf",
            TestContext.Current.CancellationToken);

        //Assert
        layer.Digest.Should().Be(HelloWorldDigest);
        layer.Size.Should().Be(11);
        layer.From.Should().Be("weights.gguf");
        File.Exists(sourcePath).Should().BeTrue();
        File.Exists(store.Paths.GetBlobPath(layer.Digest)).Should().BeTrue();
    }

    /// <summary>Importing a file whose blob is already there does not write a second copy.</summary>
    [Fact]
    public async Task CreateFromFileAsync_for_existing_blob_does_not_write_a_second_file()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        string sourcePath = Path.Combine(store.DirectoryPath, "weights.gguf");
        await File.WriteAllTextAsync(sourcePath, HelloWorld, TestContext.Current.CancellationToken);
        await LayerFactory.CreateFromTextAsync(
            store.Paths,
            HelloWorld,
            MediaTypes.Model,
            TestContext.Current.CancellationToken);

        //Act
        ModelLayer layer = await LayerFactory.CreateFromFileAsync(
            store.Paths,
            sourcePath,
            MediaTypes.Model,
            null,
            TestContext.Current.CancellationToken);

        //Assert
        layer.Digest.Should().Be(HelloWorldDigest);
        Directory.GetFiles(store.Paths.BlobsDirectory).Should().HaveCount(1);
    }

    /// <summary>Importing a file that is not there is reported as a store problem.</summary>
    [Fact]
    public async Task CreateFromFileAsync_for_missing_file_throws()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        string missing = Path.Combine(store.DirectoryPath, "not-here.gguf");

        //Act
        Func<Task> act = async () => await LayerFactory.CreateFromFileAsync(
            store.Paths,
            missing,
            MediaTypes.Model,
            null,
            TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelManagerException>();
    }

    /// <summary>A layer built from a blob already in the store takes its size from that blob.</summary>
    [Fact]
    public async Task CreateFromExistingBlobAsync_for_present_blob_sets_size_and_from()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer source = await LayerFactory.CreateFromTextAsync(
            store.Paths,
            HelloWorld,
            MediaTypes.Model,
            TestContext.Current.CancellationToken);

        //Act
        ModelLayer layer = await LayerFactory.CreateFromExistingBlobAsync(
            store.Paths,
            source.Digest,
            MediaTypes.Model,
            "llama3:latest",
            TestContext.Current.CancellationToken);

        //Assert
        layer.Digest.Should().Be(HelloWorldDigest);
        layer.Size.Should().Be(11);
        layer.MediaType.Should().Be(MediaTypes.Model);
        layer.From.Should().Be("llama3:latest");
    }

    /// <summary>A blob that is not in the store cannot become a layer.</summary>
    [Fact]
    public async Task CreateFromExistingBlobAsync_for_absent_blob_throws()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await store.Paths.EnsureDirectoriesAsync(TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = async () => await LayerFactory.CreateFromExistingBlobAsync(
            store.Paths,
            AbsentDigest,
            MediaTypes.Model,
            null,
            TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelManagerException>();
    }

    /// <summary>The message for an absent blob names the digest.</summary>
    [Fact]
    public async Task CreateFromExistingBlobAsync_for_absent_blob_message_names_the_digest()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await store.Paths.EnsureDirectoriesAsync(TestContext.Current.CancellationToken);
        ModelManagerException caught = null;

        //Act
        try
        {
            await LayerFactory.CreateFromExistingBlobAsync(
                store.Paths,
                AbsentDigest,
                MediaTypes.Model,
                null,
                TestContext.Current.CancellationToken);
        }
        catch (ModelManagerException exception)
        {
            caught = exception;
        }

        //Assert
        caught.Should().NotBeNull();
        caught.Message.Should().Be("blob " + AbsentDigest + " does not exist");
    }

    /// <summary>A blob reads back as the bytes and the text it was written from.</summary>
    [Fact]
    public async Task ReadBlobAsync_round_trips_bytes_and_text()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer layer = await LayerFactory.CreateFromTextAsync(
            store.Paths,
            HelloWorld,
            MediaTypes.Template,
            TestContext.Current.CancellationToken);

        //Act
        byte[] bytes = await LayerFactory.ReadBlobBytesAsync(
            store.Paths,
            layer.Digest,
            TestContext.Current.CancellationToken);
        string text = await LayerFactory.ReadBlobTextAsync(
            store.Paths,
            layer.Digest,
            TestContext.Current.CancellationToken);

        //Assert
        bytes.Should().Equal(Encoding.UTF8.GetBytes(HelloWorld));
        text.Should().Be(HelloWorld);
    }

    /// <summary>Reading a blob that is not in the store is reported as a store problem.</summary>
    [Fact]
    public async Task ReadBlobBytesAsync_for_absent_blob_throws()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        Func<Task> act = async () => await LayerFactory.ReadBlobBytesAsync(
            store.Paths,
            AbsentDigest,
            TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelManagerException>();
    }

    /// <summary>An intact blob verifies.</summary>
    [Fact]
    public async Task VerifyBlobAsync_for_intact_blob_returns_true()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer layer = await LayerFactory.CreateFromTextAsync(
            store.Paths,
            HelloWorld,
            MediaTypes.Model,
            TestContext.Current.CancellationToken);

        //Act
        bool verified = await LayerFactory.VerifyBlobAsync(
            store.Paths,
            layer.Digest,
            TestContext.Current.CancellationToken);

        //Assert
        verified.Should().BeTrue();
        File.Exists(store.Paths.GetBlobPath(layer.Digest)).Should().BeTrue();
    }

    /// <summary>
    /// A blob whose bytes were changed underneath the store is deleted and both digests are reported,
    /// so the caller can download it again.
    /// </summary>
    [Fact]
    public async Task VerifyBlobAsync_for_tampered_blob_deletes_it_and_throws_with_both_digests()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer layer = await LayerFactory.CreateFromTextAsync(
            store.Paths,
            HelloWorld,
            MediaTypes.Model,
            TestContext.Current.CancellationToken);
        string blobPath = store.Paths.GetBlobPath(layer.Digest);
        byte[] tampered = Encoding.UTF8.GetBytes("tampered");
        await File.WriteAllBytesAsync(blobPath, tampered, TestContext.Current.CancellationToken);
        DigestMismatchException caught = null;

        //Act
        try
        {
            await LayerFactory.VerifyBlobAsync(store.Paths, layer.Digest, TestContext.Current.CancellationToken);
        }
        catch (DigestMismatchException exception)
        {
            caught = exception;
        }

        //Assert
        caught.Should().NotBeNull();
        caught.ExpectedDigest.Should().Be(HelloWorldDigest);
        caught.ActualDigest.Should().Be(Sha256Digest.Compute(tampered));
        File.Exists(blobPath).Should().BeFalse();
    }

    /// <summary>Verifying a blob that is not in the store is reported as a store problem.</summary>
    [Fact]
    public async Task VerifyBlobAsync_for_absent_blob_throws()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        Func<Task> act = async () => await LayerFactory.VerifyBlobAsync(
            store.Paths,
            AbsentDigest,
            TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelManagerException>();
    }
}
