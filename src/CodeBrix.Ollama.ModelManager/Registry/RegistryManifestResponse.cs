using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A manifest as the registry answered it: the parsed document and the exact bytes it arrived in.
/// The raw bytes matter because the store writes the manifest file byte for byte as the registry
/// served it, so that a manifest written here hashes the same as one written by Ollama.
/// </summary>
internal sealed class RegistryManifestResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryManifestResponse"/> class.
    /// </summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="rawBytes">The bytes the registry answered with.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="manifest"/> or <paramref name="rawBytes"/> is <see langword="null"/>.
    /// </exception>
    public RegistryManifestResponse(ModelManifest manifest, byte[] rawBytes)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }
        Manifest = manifest;
        RawBytes = rawBytes;
    }

    /// <summary>The parsed manifest.</summary>
    public ModelManifest Manifest { get; }

    /// <summary>The bytes the registry answered with, unmodified.</summary>
    public byte[] RawBytes { get; }
}
