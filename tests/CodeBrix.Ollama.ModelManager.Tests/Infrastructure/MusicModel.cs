using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// One music model the live tests download: the definition the library pulls it with, the gate that
/// opens the test, and what the publisher stated about the result when the definition was written.
/// </summary>
public sealed class MusicModel
{
    /// <summary>Creates a music-model record.</summary>
    /// <param name="definition">What to pull.</param>
    /// <param name="gate">The environment variable that opens the test (its value must be "1").</param>
    /// <param name="expectedRevision">The commit the pull is pinned to, or null for a file list.</param>
    /// <param name="expectedLicenseId">The licence identifier ShowAsync is expected to report, or null when the publisher states none.</param>
    /// <param name="expectedFiles">Every file the pull is expected to produce, with its size and sha256.</param>
    public MusicModel(
        BundleDefinition definition,
        string gate,
        string expectedRevision,
        string expectedLicenseId,
        IReadOnlyList<ExpectedBundleFile> expectedFiles)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Gate = gate ?? throw new ArgumentNullException(nameof(gate));
        ExpectedRevision = expectedRevision;
        ExpectedLicenseId = expectedLicenseId;
        ExpectedFiles = expectedFiles ?? throw new ArgumentNullException(nameof(expectedFiles));
    }

    /// <summary>What to pull.</summary>
    public BundleDefinition Definition { get; }

    /// <summary>The environment variable that opens the test.</summary>
    public string Gate { get; }

    /// <summary>The commit the pull is pinned to, or null for a file list.</summary>
    public string ExpectedRevision { get; }

    /// <summary>The licence identifier ShowAsync is expected to report, or null when none is stated.</summary>
    public string ExpectedLicenseId { get; }

    /// <summary>Every file the pull is expected to produce.</summary>
    public IReadOnlyList<ExpectedBundleFile> ExpectedFiles { get; }

    /// <summary>The bytes the pull is expected to download.</summary>
    public long ExpectedBytes
    {
        get
        {
            long total = 0;
            foreach (ExpectedBundleFile file in ExpectedFiles)
            {
                total += file.Size;
            }

            return total;
        }
    }
}
