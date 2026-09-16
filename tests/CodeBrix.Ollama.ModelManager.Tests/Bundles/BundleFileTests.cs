using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the description of one file of a bundle: what it accepts, what it refuses, how it normalizes
/// a path and a hash, and the copies it makes when a source fills in what it had to ask a server for.
/// </summary>
public sealed class BundleFileTests
{
    private const string Url = "https://huggingface.co/skytnt/midi-model/resolve/94f8dc5a/model.safetensors";

    [Fact]
    public void Path_keeps_the_publishers_spelling()
        => new BundleFile("logs/version_0/events.out.tfevents.1727438892", Url).Path
            .Should().Be("logs/version_0/events.out.tfevents.1727438892");

    [Fact]
    public void Path_rewrites_backslashes_as_separators()
        => new BundleFile("logs\\version_0\\events", Url).Path.Should().Be("logs/version_0/events");

    [Fact]
    public void Path_drops_a_leading_current_directory()
        => new BundleFile("./config.json", Url).Path.Should().Be("config.json");

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.bin")]
    [InlineData("weights/../../outside.bin")]
    [InlineData("C:/weights.bin")]
    [InlineData("")]
    [InlineData("   ")]
    public void Path_that_leaves_the_bundle_is_refused(string path)
    {
        //Arrange
        Action act = () => new BundleFile(path, Url);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("model.safetensors")]
    [InlineData("not a url")]
    [InlineData("ftp://example.test/model.bin")]
    [InlineData("")]
    public void Url_that_is_not_an_http_address_is_refused(string url)
    {
        //Arrange
        Action act = () => new BundleFile("model.safetensors", url);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Url_is_parsed_as_given()
        => new BundleFile("model.safetensors", Url).Url.AbsoluteUri.Should().Be(Url);

    [Fact]
    public void Size_is_unknown_when_none_was_stated()
    {
        //Arrange
        var file = new BundleFile("config.json", Url);

        //Act and assert
        file.Size.Should().Be(BundleFile.UnknownSize);
        file.HasSize.Should().BeFalse();
    }

    [Fact]
    public void Size_of_zero_is_a_size()
    {
        //Arrange
        var file = new BundleFile("empty.txt", Url, 0);

        //Act and assert
        file.Size.Should().Be(0L);
        file.HasSize.Should().BeTrue();
    }

    [Fact]
    public void Sha256_is_lower_cased()
        => new BundleFile("model.safetensors", Url, 934043352, new string('A', 64), null, null).Sha256
            .Should().Be(new string('a', 64));

    [Theory]
    [InlineData("abc")]
    [InlineData("zz2ac8b2217f8b1c5f1e37e26e4d0f6d6b7a4c3f9e8d7c6b5a4938271605f4e3d")]
    public void Sha256_that_is_not_64_hexadecimal_characters_is_refused(string sha256)
    {
        //Arrange
        Action act = () => new BundleFile("model.safetensors", Url, 10, sha256, null, null);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Md5_that_is_not_32_hexadecimal_characters_is_refused()
    {
        //Arrange
        Action act = () => new BundleFile("model.ckpt", Url, 10, null, "af63fb", null);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HasVerifiableHash_is_true_for_either_hash()
    {
        //Arrange
        var withSha256 = new BundleFile("a.bin", Url, 1, new string('a', 64), null, null);
        var withMd5 = new BundleFile("b.ckpt", Url, 1, null, "af63fbf63ae26fe6d66140ee7c22a515", null);
        var withNeither = new BundleFile("c.json", Url, 1);

        //Act and assert
        withSha256.HasVerifiableHash.Should().BeTrue();
        withMd5.HasVerifiableHash.Should().BeTrue();
        withNeither.HasVerifiableHash.Should().BeFalse();
    }

    [Fact]
    public void GitSha1_is_kept_beside_the_content_hash()
    {
        //Arrange
        var file = new BundleFile(
            "pytorch_model.bin",
            Url,
            380166726,
            "2ecd397eac330d16e9ae27ef2bbb0bb8bbb7c7e23b152c9a52477d23fea098e9",
            null,
            "35d7676128eeed2f4fa73ec6dee6a07422d789d8");

        //Act and assert
        file.Sha256.Should().Be("2ecd397eac330d16e9ae27ef2bbb0bb8bbb7c7e23b152c9a52477d23fea098e9");
        file.GitSha1.Should().Be("35d7676128eeed2f4fa73ec6dee6a07422d789d8");
    }

    [Fact]
    public void FileName_is_the_last_segment_of_the_path()
        => new BundleFile("checkpoints/unconditional_model_16.ckpt.index", Url).FileName
            .Should().Be("unconditional_model_16.ckpt.index");

    [Fact]
    public void WithSize_replaces_the_size_and_keeps_everything_else()
    {
        //Arrange
        var file = new BundleFile("primers/fur_elise.mid", Url, BundleFile.UnknownSize, null, "afc0010e22b1c70760696cac123bbf29", null);

        //Act
        BundleFile described = file.WithSize(131);

        //Assert
        described.Size.Should().Be(131L);
        described.Path.Should().Be("primers/fur_elise.mid");
        described.Md5.Should().Be("afc0010e22b1c70760696cac123bbf29");
        file.Size.Should().Be(BundleFile.UnknownSize);
    }

    [Fact]
    public void WithMd5_replaces_the_hash_and_keeps_everything_else()
    {
        //Arrange
        var file = new BundleFile("primers/fur_elise.mid", Url, 131);

        //Act
        BundleFile described = file.WithMd5("AFC0010E22B1C70760696CAC123BBF29");

        //Assert
        described.Md5.Should().Be("afc0010e22b1c70760696cac123bbf29");
        described.Size.Should().Be(131L);
        file.Md5.Should().BeNull();
    }

    [Fact]
    public void ToString_names_the_path_and_the_size()
        => new BundleFile("config.json", Url, 2016).ToString().Should().Be("config.json (2016 bytes)");

    [Fact]
    public void ToString_names_only_the_path_when_the_size_is_unknown()
        => new BundleFile("config.json", Url).ToString().Should().Be("config.json");
}
