namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What laying a bundle's file tree out on disk is asked to do: how each file is put in place, and what
/// happens when something is already there.
/// </summary>
public sealed class MaterializeOptions
{
    /// <summary>
    /// How each file is written. The default is <see cref="MaterializeLink.Hardlink"/>, which costs no
    /// disk space and falls back to a copy on its own when the platform or the file system will not
    /// link.
    /// </summary>
    public MaterializeLink Link { get; set; } = MaterializeLink.Hardlink;

    /// <summary>
    /// Whether a file already at one of the target paths is replaced. The default is
    /// <see langword="false"/>: such a file fails the call and nothing further is written, so a
    /// directory that holds work of its own is never overwritten by accident.
    /// </summary>
    public bool Overwrite { get; set; }
}
