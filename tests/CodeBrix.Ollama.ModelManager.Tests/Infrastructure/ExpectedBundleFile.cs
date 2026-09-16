using System;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>A file a live pull is expected to produce: its publisher path, size and sha256.</summary>
public sealed class ExpectedBundleFile
{
    /// <summary>Creates the expectation.</summary>
    /// <param name="path">The publisher's relative path.</param>
    /// <param name="size">The size in bytes.</param>
    /// <param name="sha256">The lower-case hexadecimal sha256 of the content.</param>
    public ExpectedBundleFile(string path, long size, string sha256)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Size = size;
        Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
    }

    /// <summary>The publisher's relative path.</summary>
    public string Path { get; }

    /// <summary>The size in bytes.</summary>
    public long Size { get; }

    /// <summary>The lower-case hexadecimal sha256 of the content.</summary>
    public string Sha256 { get; }
}
