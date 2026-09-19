namespace ModelQueryTool.ModelAccess;

/// <summary>
/// What one walk of a folder found: how many bytes lie under it, and whether there was a file at all.
/// The two are not the same question, because an empty file is presence and no bytes.
/// </summary>
internal sealed class StoreContents
{
    /// <summary>Gets how many bytes lie under the folder.</summary>
    public long Bytes { get; private set; }

    /// <summary>Gets whether the walk found a file, at any depth.</summary>
    public bool HoldsAnyFile { get; private set; }

    /// <summary>
    /// Counts one more file.
    /// </summary>
    /// <param name="length">The length of the file in bytes.</param>
    public void Add(long length)
    {
        Bytes += length;
        HoldsAnyFile = true;
    }
}
