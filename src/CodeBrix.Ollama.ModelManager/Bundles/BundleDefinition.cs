using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The description of one model that is not a GGUF file on an Ollama-protocol registry: the name it is
/// stored under, where its files come from, which of them are wanted, and what its publisher states
/// about its licence. This is the vocabulary a consumer describes its own models in; the library ships
/// no definitions of its own and names no particular model anywhere.
/// </summary>
/// <remarks>
/// A definition is checked when it is built, so a definition that exists is one a pull can be started
/// from: the name parses in the store's own name grammar, a Hugging Face definition names a repository,
/// and a file-list definition carries at least one file.
/// </remarks>
public sealed class BundleDefinition
{
    /// <summary>
    /// Initializes a definition and checks it.
    /// </summary>
    /// <param name="name">
    /// The name the bundle is stored under, in the store's name grammar, for example
    /// <c>hf.co/m-a-p/MuPT-v1-8192-190M:main</c>.
    /// </param>
    /// <param name="source">Where the files come from.</param>
    /// <param name="repository">
    /// For <see cref="PullSource.HuggingFaceFiles"/>, the repository as
    /// <c>&lt;namespace&gt;/&lt;repository&gt;</c>. Ignored by the other sources.
    /// </param>
    /// <param name="revision">
    /// For <see cref="PullSource.HuggingFaceFiles"/>, the branch, tag or commit to list;
    /// <see langword="null"/> or empty means <see cref="PullOptions.DefaultRevision"/>. Ignored by the
    /// other sources.
    /// </param>
    /// <param name="files">
    /// For <see cref="PullSource.FileList"/>, the files to pull. Ignored by the other sources.
    /// </param>
    /// <param name="filter">Which listed files are wanted, or <see langword="null"/> for all of them.</param>
    /// <param name="license">
    /// What the publisher states about the licence, or <see langword="null"/> for
    /// <see cref="LicenseRecord.None"/>.
    /// </param>
    /// <param name="notes">Anything a human should know about this bundle, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">
    /// The name is missing; a Hugging Face definition has no repository or one that is not
    /// <c>&lt;namespace&gt;/&lt;repository&gt;</c>; or a file-list definition has no files.
    /// </exception>
    /// <exception cref="InvalidModelNameException">The name does not parse.</exception>
    public BundleDefinition(
        string name,
        PullSource source,
        string repository,
        string revision,
        IReadOnlyList<BundleFile> files,
        FileFilter filter,
        LicenseRecord license,
        string notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A bundle definition needs a name.", nameof(name));
        }

        // The name is checked here rather than at the far end of a pull, in the same grammar and with
        // the same exception the store itself uses.
        if (!ModelName.TryParse(name.Trim(), out ModelName _))
        {
            throw new InvalidModelNameException(name, "invalid model name");
        }

        Name = name.Trim();
        Source = source;
        Repository = CheckRepository(source, repository);
        Revision = string.IsNullOrWhiteSpace(revision) ? PullOptions.DefaultRevision : revision.Trim();
        Files = CheckFiles(source, files);
        Filter = filter ?? FileFilter.Default;
        License = license ?? LicenseRecord.None;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>The name the bundle is stored under.</summary>
    public string Name { get; }

    /// <summary>Where the files come from.</summary>
    public PullSource Source { get; }

    /// <summary>
    /// The Hugging Face repository as <c>&lt;namespace&gt;/&lt;repository&gt;</c>, or
    /// <see langword="null"/> for the other sources.
    /// </summary>
    public string Repository { get; }

    /// <summary>
    /// The branch, tag or commit a Hugging Face listing asks for.
    /// <see cref="PullOptions.DefaultRevision"/> when nothing was said.
    /// </summary>
    public string Revision { get; }

    /// <summary>
    /// The files of a file-list definition, in the order they were given, or an empty list for the
    /// other sources. Never <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<BundleFile> Files { get; }

    /// <summary>Which of the listed files are wanted. Never <see langword="null"/>.</summary>
    public FileFilter Filter { get; }

    /// <summary>What the publisher states about the licence. Never <see langword="null"/>.</summary>
    public LicenseRecord License { get; }

    /// <summary>A free-text note, or <see langword="null"/>.</summary>
    public string Notes { get; }

    /// <summary>
    /// Describes a model in a Hugging Face file repository.
    /// </summary>
    /// <param name="name">The name the bundle is stored under.</param>
    /// <param name="repository">The repository as <c>&lt;namespace&gt;/&lt;repository&gt;</c>.</param>
    /// <param name="revision">The branch, tag or commit, or <see langword="null"/> for the default branch.</param>
    /// <param name="filter">Which files are wanted, or <see langword="null"/> for all of them.</param>
    /// <param name="license">What the publisher states, or <see langword="null"/> for nothing.</param>
    /// <param name="notes">A free-text note, or <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">The name or the repository is missing or not usable.</exception>
    public static BundleDefinition ForHuggingFace(
        string name, string repository, string revision, FileFilter filter, LicenseRecord license, string notes)
    {
        return new BundleDefinition(
            name, PullSource.HuggingFaceFiles, repository, revision, null, filter, license, notes);
    }

    /// <summary>
    /// Describes a model as a list of addresses, which is the route for a storage bucket or any other
    /// plain HTTPS host.
    /// </summary>
    /// <param name="name">The name the bundle is stored under.</param>
    /// <param name="files">The files to pull.</param>
    /// <param name="filter">Which of them are wanted, or <see langword="null"/> for all of them.</param>
    /// <param name="license">What the publisher states, or <see langword="null"/> for nothing.</param>
    /// <param name="notes">A free-text note, or <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">The name is missing, or there are no files.</exception>
    public static BundleDefinition ForFileList(
        string name, IReadOnlyList<BundleFile> files, FileFilter filter, LicenseRecord license, string notes)
    {
        return new BundleDefinition(name, PullSource.FileList, null, null, files, filter, license, notes);
    }

    /// <summary>
    /// Describes a model on a registry that speaks Ollama's protocol, which is the source a plain pull
    /// uses. Such a definition exists so that a catalogue can hold every kind of model in one list.
    /// </summary>
    /// <param name="name">The name the bundle is stored under and pulled by.</param>
    /// <param name="license">What the publisher states, or <see langword="null"/> for nothing.</param>
    /// <param name="notes">A free-text note, or <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">The name is missing.</exception>
    public static BundleDefinition ForRegistry(string name, LicenseRecord license, string notes)
    {
        return new BundleDefinition(name, PullSource.Registry, null, null, null, null, license, notes);
    }

    /// <summary>
    /// The options a pull of this definition is started with.
    /// </summary>
    /// <returns>
    /// Options that carry the source, the repository, the revision, the files and the filter of this
    /// definition. The licence and the notes are not part of them: they describe the bundle, and a pull
    /// reports what the source itself states rather than what a definition claims.
    /// </returns>
    public PullOptions ToPullOptions()
    {
        return new PullOptions
        {
            Source = Source,
            Repository = Repository,
            Revision = Source == PullSource.HuggingFaceFiles ? Revision : null,
            Files = Files,
            Filter = Filter
        };
    }

    /// <summary>
    /// Returns the name and the source.
    /// </summary>
    /// <returns>A one-line description of the definition.</returns>
    public override string ToString()
    {
        return Name + " (" + Source + ")";
    }

    /// <summary>
    /// Checks the repository of a Hugging Face definition and ignores it for the other sources.
    /// </summary>
    /// <param name="source">The source the definition names.</param>
    /// <param name="repository">The repository as it was supplied.</param>
    /// <returns>The trimmed repository, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">A Hugging Face definition has no usable repository.</exception>
    private static string CheckRepository(PullSource source, string repository)
    {
        if (source != PullSource.HuggingFaceFiles)
        {
            return string.IsNullOrWhiteSpace(repository) ? null : repository.Trim();
        }

        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException(
                "A Hugging Face bundle definition needs a repository, as <namespace>/<repository>.",
                nameof(repository));
        }

        string trimmed = repository.Trim().Trim('/');
        string[] parts = trimmed.Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ArgumentException(
                "The Hugging Face repository '" + repository + "' is not <namespace>/<repository>.",
                nameof(repository));
        }

        return trimmed;
    }

    /// <summary>
    /// Checks the files of a file-list definition and ignores them for the other sources.
    /// </summary>
    /// <param name="source">The source the definition names.</param>
    /// <param name="files">The files as they were supplied.</param>
    /// <returns>A copy of the files, or an empty list.</returns>
    /// <exception cref="ArgumentException">A file-list definition has no files, or one of them is null.</exception>
    private static IReadOnlyList<BundleFile> CheckFiles(PullSource source, IReadOnlyList<BundleFile> files)
    {
        if (source != PullSource.FileList)
        {
            return files == null ? Array.Empty<BundleFile>() : new List<BundleFile>(files).AsReadOnly();
        }

        if (files == null || files.Count == 0)
        {
            throw new ArgumentException(
                "A file-list bundle definition needs at least one file.", nameof(files));
        }

        var copy = new List<BundleFile>(files.Count);
        foreach (BundleFile file in files)
        {
            if (file == null)
            {
                throw new ArgumentException(
                    "A file-list bundle definition cannot carry a null file.", nameof(files));
            }
            copy.Add(file);
        }
        return copy.AsReadOnly();
    }
}
