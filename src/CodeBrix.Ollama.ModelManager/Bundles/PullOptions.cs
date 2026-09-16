using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a bundle pull is asked to do: where the files come from, which revision of them, which of them
/// are wanted, and how strict the verification is. A pull that is given no options at all behaves
/// exactly as a plain registry pull does.
/// </summary>
/// <remarks>
/// Every property here is read by the pull that takes these options; nothing is read anywhere else. A
/// property that is not set falls back to the value documented on it, so an instance built with an
/// object initializer that sets only what it cares about is always usable.
/// </remarks>
public sealed class PullOptions
{
    /// <summary>
    /// The revision a Hugging Face pull uses when the caller names none: the default branch.
    /// </summary>
    public const string DefaultRevision = "main";

    private IReadOnlyList<BundleFile> _files = Array.Empty<BundleFile>();
    private FileFilter _filter = FileFilter.Default;

    /// <summary>
    /// Where the files come from. The default is <see cref="PullSource.Registry"/>, which is the
    /// behaviour of a pull with no options.
    /// </summary>
    public PullSource Source { get; set; } = PullSource.Registry;

    /// <summary>
    /// For <see cref="PullSource.HuggingFaceFiles"/>, the repository as
    /// <c>&lt;namespace&gt;/&lt;repository&gt;</c>. When it is <see langword="null"/> the pull takes the
    /// repository from the model name, whose namespace and model part spell the same thing. Ignored by
    /// the other sources.
    /// </summary>
    public string Repository { get; set; }

    /// <summary>
    /// For <see cref="PullSource.HuggingFaceFiles"/>, the branch, tag or commit to list. When it is
    /// <see langword="null"/> the pull takes the model name's tag, and the tag <c>latest</c> means
    /// <see cref="DefaultRevision"/>. The pull records the commit the revision resolved to, never the
    /// moving name. Ignored by the other sources.
    /// </summary>
    public string Revision { get; set; }

    /// <summary>
    /// For <see cref="PullSource.FileList"/>, the files to pull. Never <see langword="null"/>: an
    /// unset list reads as empty, and a pull from a file list with no files is refused.
    /// </summary>
    public IReadOnlyList<BundleFile> Files
    {
        get { return _files; }
        set { _files = value ?? Array.Empty<BundleFile>(); }
    }

    /// <summary>
    /// Which of the listed files are pulled. Never <see langword="null"/>: an unset filter reads as
    /// <see cref="FileFilter.Default"/>, which keeps everything the source lists.
    /// </summary>
    public FileFilter Filter
    {
        get { return _filter; }
        set { _filter = value ?? FileFilter.Default; }
    }

    /// <summary>
    /// Whether a file the source states no hash for fails the pull. The default is
    /// <see langword="false"/>: such a file is downloaded, the store computes its SHA-256 and names the
    /// blob by it, so a second pull verifies against the first. Set it when only content a publisher
    /// vouched for is acceptable.
    /// </summary>
    public bool RequireHashes { get; set; }

    /// <summary>
    /// Options for a plain registry pull, which is what a pull without options does.
    /// </summary>
    /// <returns>The options.</returns>
    public static PullOptions ForRegistry()
    {
        return new PullOptions { Source = PullSource.Registry };
    }

    /// <summary>
    /// Options for a pull from a Hugging Face file repository.
    /// </summary>
    /// <param name="repository">
    /// The repository as <c>&lt;namespace&gt;/&lt;repository&gt;</c>, or <see langword="null"/> to take
    /// it from the model name.
    /// </param>
    /// <param name="revision">
    /// The branch, tag or commit, or <see langword="null"/> to take it from the model name's tag.
    /// </param>
    /// <param name="filter">Which files are wanted, or <see langword="null"/> for all of them.</param>
    /// <returns>The options.</returns>
    public static PullOptions ForHuggingFace(string repository, string revision, FileFilter filter)
    {
        return new PullOptions
        {
            Source = PullSource.HuggingFaceFiles,
            Repository = repository,
            Revision = revision,
            Filter = filter
        };
    }

    /// <summary>
    /// Options for a pull of an explicit list of addresses.
    /// </summary>
    /// <param name="files">The files to pull.</param>
    /// <param name="filter">Which of them are wanted, or <see langword="null"/> for all of them.</param>
    /// <returns>The options.</returns>
    public static PullOptions ForFileList(IReadOnlyList<BundleFile> files, FileFilter filter)
    {
        return new PullOptions
        {
            Source = PullSource.FileList,
            Files = files,
            Filter = filter
        };
    }
}
