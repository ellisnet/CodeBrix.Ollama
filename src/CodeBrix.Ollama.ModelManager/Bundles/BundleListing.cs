using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a source answers when it is asked what a bundle holds, before anything is downloaded: which
/// files there are, how large they are together, which revision the listing was taken at, and what the
/// source states about the licence.
/// </summary>
/// <remarks>
/// The files are already filtered: what a listing shows is what a pull of the same source with the same
/// filter would fetch, so a caller can report the size of a pull before starting it.
/// </remarks>
public sealed class BundleListing
{
    private readonly IReadOnlyList<BundleFile> _files;

    /// <summary>
    /// Initializes a listing.
    /// </summary>
    /// <param name="repositoryId">
    /// What the source calls the thing that was listed - <c>&lt;namespace&gt;/&lt;repository&gt;</c> for
    /// a Hugging Face repository, the bucket and prefix for a storage bucket - or
    /// <see langword="null"/> for a plain list of addresses.
    /// </param>
    /// <param name="resolvedRevision">
    /// The commit the listing was taken at, or <see langword="null"/> when the source has no such
    /// notion. A Hugging Face listing always carries one, and it is a commit rather than the branch or
    /// tag that was asked for.
    /// </param>
    /// <param name="license">
    /// What the source states about the licence, or <see langword="null"/> for
    /// <see cref="LicenseRecord.None"/>.
    /// </param>
    /// <param name="files">The files, already filtered, in the order the source listed them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    public BundleListing(
        string repositoryId, string resolvedRevision, LicenseRecord license, IReadOnlyList<BundleFile> files)
    {
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        RepositoryId = string.IsNullOrWhiteSpace(repositoryId) ? null : repositoryId.Trim();
        ResolvedRevision = string.IsNullOrWhiteSpace(resolvedRevision) ? null : resolvedRevision.Trim();
        License = license ?? LicenseRecord.None;
        _files = new List<BundleFile>(files).AsReadOnly();

        long total = 0;
        foreach (BundleFile file in _files)
        {
            if (file != null && file.HasSize)
            {
                total += file.Size;
            }
        }
        TotalBytes = total;
    }

    /// <summary>
    /// What the source calls the thing that was listed, or <see langword="null"/> for a plain list.
    /// </summary>
    public string RepositoryId { get; }

    /// <summary>
    /// The commit the listing was taken at, or <see langword="null"/> when the source has no commits.
    /// </summary>
    public string ResolvedRevision { get; }

    /// <summary>What the source states about the licence. Never <see langword="null"/>.</summary>
    public LicenseRecord License { get; }

    /// <summary>
    /// The SPDX identifier the source states, or <see langword="null"/>. A shortcut for
    /// <see cref="LicenseRecord.LicenseId"/> of <see cref="License"/>.
    /// </summary>
    public string LicenseId
    {
        get { return License.LicenseId; }
    }

    /// <summary>
    /// The address the licence statement was read from, or <see langword="null"/>. A shortcut for
    /// <see cref="LicenseRecord.LicenseSource"/> of <see cref="License"/>.
    /// </summary>
    public string LicenseSource
    {
        get { return License.LicenseSource; }
    }

    /// <summary>The files the pull would fetch, already filtered.</summary>
    public IReadOnlyList<BundleFile> Files
    {
        get { return _files; }
    }

    /// <summary>
    /// The sum of the stated sizes in bytes. A file whose size the source did not state contributes
    /// nothing, so this is a floor rather than a promise when
    /// <see cref="BundleFile.HasSize"/> is false anywhere in <see cref="Files"/>.
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// Returns what was listed, how many files it holds and how large they are.
    /// </summary>
    /// <returns>A one-line description of the listing.</returns>
    public override string ToString()
    {
        string what = RepositoryId ?? "file list";
        if (ResolvedRevision != null)
        {
            what += "@" + ResolvedRevision;
        }
        return what + ": " + _files.Count.ToString(CultureInfo.InvariantCulture) + " files, "
            + TotalBytes.ToString(CultureInfo.InvariantCulture) + " bytes";
    }
}
