using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="Sha256Digest"/>. The digest values are the published sha256 test vectors, so
/// they double as a check that the library spells a digest the way Ollama does.
/// </summary>
public sealed class Sha256DigestTests
{
    private const string EmptyDigest = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private const string AbcDigest = "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private const string SixtyFourHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>The prefix is what a manifest spells a digest with.</summary>
    [Fact]
    public void Prefix_is_colon_form()
        => Sha256Digest.Prefix.Should().Be("sha256:");

    /// <summary>Hashing bytes already in memory produces the known vectors.</summary>
    [Theory]
    [InlineData("", EmptyDigest)]
    [InlineData("abc", AbcDigest)]
    public void Compute_for_known_input_returns_known_digest(string input, string expected)
        => Sha256Digest.Compute(Encoding.UTF8.GetBytes(input)).Should().Be(expected);

    /// <summary>Hashing a stream produces the same digest as hashing its bytes.</summary>
    [Fact]
    public async Task ComputeAsync_for_stream_returns_known_digest()
    {
        //Arrange
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("abc"));

        //Act
        string digest = await Sha256Digest.ComputeAsync(stream, TestContext.Current.CancellationToken);

        //Assert
        digest.Should().Be(AbcDigest);
    }

    /// <summary>Hashing a file produces the same digest as hashing its bytes.</summary>
    [Fact]
    public async Task ComputeFileAsync_for_file_returns_known_digest()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        string path = Path.Combine(store.DirectoryPath, "abc.txt");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("abc"), TestContext.Current.CancellationToken);

        //Act
        string digest = await Sha256Digest.ComputeFileAsync(path, TestContext.Current.CancellationToken);

        //Assert
        digest.Should().Be(AbcDigest);
    }

    /// <summary>Both the manifest spelling and the file name spelling are valid, in either case.</summary>
    [Theory]
    [InlineData("sha256:" + SixtyFourHex)]
    [InlineData("sha256-" + SixtyFourHex)]
    [InlineData("sha256:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    public void IsValid_for_well_formed_digest_returns_true(string digest)
        => Sha256Digest.IsValid(digest).Should().BeTrue();

    /// <summary>Anything that is not exactly the algorithm, a separator and 64 hex characters is rejected.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:")]
    [InlineData(SixtyFourHex)]
    [InlineData("sha256_" + SixtyFourHex)]
    [InlineData("sha256:" + SixtyFourHex + "0")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [InlineData("sha512:" + SixtyFourHex)]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    [InlineData("sha256-" + SixtyFourHex + ".codebrix-partial")]
    public void IsValid_for_malformed_digest_returns_false(string digest)
        => Sha256Digest.IsValid(digest).Should().BeFalse();

    /// <summary>A manifest digest becomes a blob file name by way of the separator.</summary>
    [Fact]
    public void ToFileName_for_manifest_digest_replaces_colon_with_hyphen()
        => Sha256Digest.ToFileName("sha256:" + SixtyFourHex).Should().Be("sha256-" + SixtyFourHex);

    /// <summary>A digest already spelled as a file name is left alone.</summary>
    [Fact]
    public void ToFileName_for_file_name_digest_returns_it_unchanged()
        => Sha256Digest.ToFileName("sha256-" + SixtyFourHex).Should().Be("sha256-" + SixtyFourHex);

    /// <summary>A blob file name becomes a manifest digest by way of the separator.</summary>
    [Fact]
    public void ToDigest_for_file_name_replaces_hyphen_with_colon()
        => Sha256Digest.ToDigest("sha256-" + SixtyFourHex).Should().Be("sha256:" + SixtyFourHex);

    /// <summary>A digest already spelled for a manifest is left alone.</summary>
    [Fact]
    public void ToDigest_for_manifest_digest_returns_it_unchanged()
        => Sha256Digest.ToDigest("sha256:" + SixtyFourHex).Should().Be("sha256:" + SixtyFourHex);

    /// <summary>The short form is the 12 hexadecimal characters Ollama prints on a progress line.</summary>
    [Fact]
    public void Short_for_digest_returns_twelve_hex_characters()
    {
        //Act
        string shortForm = Sha256Digest.Short(AbcDigest);

        //Assert
        shortForm.Should().Be("ba7816bf8f01");
        shortForm.Length.Should().Be(12);
    }

    /// <summary>A string too short to hold a digest comes back unchanged rather than throwing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("sha256:abc")]
    public void Short_for_too_short_input_returns_it_unchanged(string input)
        => Sha256Digest.Short(input).Should().Be(input);

    /// <summary>A null stream is rejected.</summary>
    [Fact]
    public async Task ComputeAsync_with_null_stream_throws()
    {
        //Act
        Func<Task> act = async () => await Sha256Digest.ComputeAsync(null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
