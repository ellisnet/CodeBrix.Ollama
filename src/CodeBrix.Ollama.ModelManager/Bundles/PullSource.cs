namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Where the files of a pull come from. The value chooses the wire protocol and the listing step; it
/// does not change how the files are stored, because every source ends in the same content-addressed
/// blob and manifest layout.
/// </summary>
public enum PullSource
{
    /// <summary>
    /// A registry that speaks Ollama's manifest-and-blob protocol, which is what a plain
    /// <c>PullAsync(name)</c> uses. The manifest names the layers and the layers are GGUF files.
    /// </summary>
    Registry = 0,

    /// <summary>
    /// A Hugging Face file repository, listed through the Hub API and fetched over plain HTTPS. The
    /// commit the listing resolved to is recorded, so a later pull of the same tag can tell "unchanged"
    /// from "moved".
    /// </summary>
    HuggingFaceFiles = 1,

    /// <summary>
    /// An explicit list of addresses, each with the relative path it takes inside the bundle. A public
    /// storage bucket is listed into such a list first.
    /// </summary>
    FileList = 2
}
