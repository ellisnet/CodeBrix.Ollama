// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: server/download.go at commit a43fad18.
using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One byte range of a blob download: where it starts, how long it is, and how much of it has already
/// been written to the partial file. This is the C# equivalent of Ollama's <c>blobDownloadPart</c>,
/// minus the per-part file handle: every part of a download here is recorded in one state sidecar
/// rather than in a file of its own.
/// </summary>
internal sealed class BlobDownloadPart
{
    /// <summary>The zero-based index of the part within the download.</summary>
    [JsonPropertyName("n")]
    public int N { get; set; }

    /// <summary>The offset of the first byte of the part within the blob.</summary>
    [JsonPropertyName("offset")]
    public long Offset { get; set; }

    /// <summary>The length of the part in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// How many bytes of the part are already on disk. A resumed part asks the registry for the range
    /// that starts at <see cref="Offset"/> plus this value.
    /// </summary>
    [JsonPropertyName("completed")]
    public long Completed { get; set; }

    /// <summary>The offset of the next byte to fetch: <see cref="Offset"/> plus <see cref="Completed"/>.</summary>
    [JsonIgnore]
    public long StartsAt
    {
        get { return Offset + Completed; }
    }

    /// <summary>The offset one past the last byte of the part: <see cref="Offset"/> plus <see cref="Size"/>.</summary>
    [JsonIgnore]
    public long StopsAt
    {
        get { return Offset + Size; }
    }
}
