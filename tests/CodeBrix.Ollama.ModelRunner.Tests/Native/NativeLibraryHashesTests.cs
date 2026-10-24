using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the compiled-in table of native library hashes: it agrees with every native the package ships, in
/// both directions, so a native rebuilt and adopted without regenerating the table fails the suite here
/// rather than at a user's first load.
/// </summary>
public sealed class NativeLibraryHashesTests
{
    /// <summary>Every shipped native has a line in the table, and its bytes hash to exactly that line.</summary>
    [Fact]
    public void Every_shipped_native_matches_its_recorded_hash()
    {
        //Arrange
        IReadOnlyList<string> natives = ShippedNatives();

        //Act and assert
        natives.Should().HaveCount(7);
        foreach (string path in natives)
        {
            string fileName = Path.GetFileName(path);
            string runtimeIdentifier = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
            string actual;
            using (FileStream bytes = File.OpenRead(path)) actual = NativeLibraryHashes.Compute(bytes);

            string expected = NativeLibraryHashes.ExpectedFor(runtimeIdentifier, fileName);
            (runtimeIdentifier + "/" + fileName + " " + actual)
                .Should().Be(runtimeIdentifier + "/" + fileName + " " + expected);
        }
    }

    /// <summary>The table names no native the package does not ship.</summary>
    [Fact]
    public void Every_recorded_hash_names_a_shipped_native()
    {
        //Arrange
        HashSet<string> shipped = ShippedNatives()
            .Select(path => NativeLibraryHashes.Key(
                Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))), Path.GetFileName(path)))
            .ToHashSet(StringComparer.Ordinal);

        //Act and assert
        foreach (string key in NativeLibraryHashes.Recorded.Keys) shipped.Should().Contain(key);
        NativeLibraryHashes.Recorded.Should().HaveCount(shipped.Count);
    }

    /// <summary>The table reads the plain sha256sum format, with comments and blank lines skipped.</summary>
    [Fact]
    public void Parse_reads_sha256sum_output()
    {
        //Arrange
        string hash = new string('A', 64);
        string text = "# comment\n\n" + hash + "  linux-x64/native/libcodebrix_llama.so\n"
            + new string('b', 64) + " *win-x64/native/codebrix_llama.dll\n";

        //Act
        IReadOnlyDictionary<string, string> table = NativeLibraryHashes.Parse(new StringReader(text));

        //Assert
        table.Should().HaveCount(2);
        table["linux-x64/libcodebrix_llama.so"].Should().Be(new string('a', 64));
        table["win-x64/codebrix_llama.dll"].Should().Be(new string('b', 64));
    }

    /// <summary>A malformed or duplicated line is a broken table, never a silently shorter one.</summary>
    /// <param name="text">The table text.</param>
    [Theory]
    [InlineData("abc  linux-x64/native/libcodebrix_llama.so")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  libcodebrix_llama.so")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  linux-x64/lib/libcodebrix_llama.so")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  linux-x64/native/a.so\n"
        + "1111111111111111111111111111111111111111111111111111111111111111  linux-x64/native/a.so")]
    public void Parse_refuses_a_malformed_table(string text)
    {
        //Act
        Action act = () => NativeLibraryHashes.Parse(new StringReader(text));

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    /// <summary>Hashing reads the stream to its end and writes lowercase hexadecimal.</summary>
    [Fact]
    public void Compute_returns_lowercase_sha256()
    {
        //Arrange
        byte[] content = { 1, 2, 3, 4 };

        //Act
        string actual = NativeLibraryHashes.Compute(new MemoryStream(content));

        //Assert
        actual.Should().Be(Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    //The natives the test output carries are the project's runtimes/ tree, copied with its layout intact by
    //the ModelRunner project's staging target - so this is the set the package ships.
    private static IReadOnlyList<string> ShippedNatives()
    {
        string runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
        return Directory.EnumerateFiles(runtimes, "*codebrix_llama.*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "native")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }
}
