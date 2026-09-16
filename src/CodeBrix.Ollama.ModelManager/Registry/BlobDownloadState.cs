using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama server/download.go;

/// <summary>
/// The sidecar document written beside a partially downloaded blob. It records which byte ranges the
/// blob was split into and how much of each one is already on disk, so an interrupted download can be
/// resumed from where it stopped instead of starting over.
/// </summary>
/// <remarks>
/// Ollama writes one small JSON file per part, named <c>&lt;blob&gt;-partial-&lt;n&gt;</c>. This port
/// writes a single file describing every part, which keeps the number of files in the store down and
/// makes the resume check a single read. The fields carry the same information.
/// </remarks>
internal sealed class BlobDownloadState
{
    /// <summary>The digest of the blob being downloaded, in <c>sha256:&lt;hex&gt;</c> form.</summary>
    [JsonPropertyName("digest")]
    public string Digest { get; set; }

    /// <summary>The size of the whole blob in bytes.</summary>
    [JsonPropertyName("total")]
    public long Total { get; set; }

    /// <summary>The parts the blob was split into, in offset order.</summary>
    [JsonPropertyName("parts")]
    public List<BlobDownloadPart> Parts { get; set; } = new List<BlobDownloadPart>();
}
