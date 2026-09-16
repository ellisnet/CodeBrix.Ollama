namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// How a materialized file is put in place beside the blob it comes from.
/// </summary>
public enum MaterializeLink
{
    /// <summary>
    /// A hard link to the blob, which costs no disk space and no copying time. A link that the platform
    /// or the file system will not make - across a volume, or on a file system without hard links -
    /// becomes a copy by itself, so this is always usable.
    /// </summary>
    Hardlink = 0,

    /// <summary>
    /// A copy of the blob. It costs the space and the time, and the written file is then independent of
    /// the store: deleting the model afterwards leaves it standing.
    /// </summary>
    Copy = 1,

    /// <summary>
    /// A symbolic link to the blob. It is never chosen by itself and never falls back to anything: a
    /// caller that asks for symbolic links wants the store's own file to be what a reader ends up at,
    /// and silently writing a copy instead would hide that it did not happen.
    /// </summary>
    Symlink = 2
}
