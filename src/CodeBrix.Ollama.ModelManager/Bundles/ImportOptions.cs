namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What an import of a folder already on disk is asked to do: which of the files under it are wanted,
/// whether they are linked into the store rather than copied, and what the importer knows about the
/// licence. This is the route for anything a publisher keeps behind a sign-in: fetch it however it has
/// to be fetched, then hand the folder to the store.
/// </summary>
/// <remarks>
/// Every property has a value that is safe when nothing is said, so an instance built with an object
/// initializer that sets only what it cares about is always usable.
/// </remarks>
public sealed class ImportOptions
{
    private FileFilter _filter = FileFilter.Default;

    /// <summary>
    /// Which of the files under the folder are imported. Never <see langword="null"/>: an unset filter
    /// reads as <see cref="FileFilter.Default"/>, which keeps every file the walk finds. A licence or a
    /// readme is kept whatever the filter says.
    /// </summary>
    public FileFilter Filter
    {
        get { return _filter; }
        set { _filter = value ?? FileFilter.Default; }
    }

    /// <summary>
    /// Whether a file is hard-linked into the store's blobs directory instead of copied. The default is
    /// <see langword="false"/>, which copies.
    /// </summary>
    /// <remarks>
    /// A link costs no disk space and no copying time, and it is the right choice for a folder that is
    /// about to be thrown away. It cannot cross a volume or a file system that will not link, and the
    /// import falls back to a copy by itself when that happens. What it does mean is that the file and
    /// the blob are then the same bytes on disk: editing the file in place afterwards edits the blob,
    /// which is why copying, not linking, is what an unset option does.
    /// </remarks>
    public bool Link { get; set; }

    /// <summary>
    /// What the importer knows about the licence, or <see langword="null"/>. When it is
    /// <see langword="null"/> and the folder holds a <c>LICENSE</c> file, the import records that file
    /// as the place the terms are stated and states no identifier of its own.
    /// </summary>
    public LicenseRecord License { get; set; }
}
