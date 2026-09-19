using System;
using CodeBrix.Ollama.ModelManager;

namespace ModelQueryTool.ModelAccess.Models;

/// <summary>
/// One model this library knows how to obtain: the name its registry serves it under, a name to show a
/// person, how many bytes the whole download comes to, and the digest its weights are expected to carry.
/// </summary>
/// <remarks>
/// The expected total is what a progress bar divides by before the first manifest has arrived, and the
/// expected digest is REPORTED and never enforced: a publisher who re-uploads a model changes it, and a
/// staged model whose digest no longer matches is still a staged model.
/// </remarks>
public sealed class ModelDescriptor
{
    /// <summary>The number of characters a sha256 digest has when it is written as plain hexadecimal.</summary>
    private const int Sha256HexLength = 64;

    /// <summary>
    /// Describes a model, checking every argument, so that a descriptor that exists is one this library
    /// can act on.
    /// </summary>
    /// <param name="name">The store name, which must parse as a fully qualified model name.</param>
    /// <param name="displayName">The name to show a person.</param>
    /// <param name="expectedTotalBytes">
    /// The size of the whole download in bytes, or zero when it is not known in advance.
    /// </param>
    /// <param name="weightsSha256">
    /// The digest the model-weights layer is expected to carry, as lower-case hexadecimal with no
    /// <c>sha256:</c> prefix, or <see langword="null"/> when nothing is expected.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The name is blank or does not parse as a fully qualified model name, the display name is blank,
    /// or the digest is not sixty-four hexadecimal characters.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The expected total is negative.</exception>
    public ModelDescriptor(
        string name,
        string displayName,
        long expectedTotalBytes = 0L,
        string weightsSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A store name is required.", nameof(name));
        }
        if (!ModelName.TryParse(name, out ModelName parsed) || !parsed.IsFullyQualified)
        {
            throw new ArgumentException(
                "The store name '" + name + "' is not a valid, fully qualified model name.",
                nameof(name));
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A display name is required.", nameof(displayName));
        }
        if (expectedTotalBytes < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedTotalBytes),
                expectedTotalBytes,
                "The expected total cannot be negative.");
        }

        Name = name;
        DisplayName = displayName;
        ExpectedTotalBytes = expectedTotalBytes;
        WeightsSha256 = NormalizeDigest(weightsSha256);
    }

    /// <summary>Gets the name the registry serves the model under, as the store spells it.</summary>
    public string Name { get; }

    /// <summary>Gets the name to show a person.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the size of the whole download in bytes, or zero when it is not known before the manifest
    /// arrives.
    /// </summary>
    public long ExpectedTotalBytes { get; }

    /// <summary>
    /// Gets the digest the model-weights layer is expected to carry, as lower-case hexadecimal with no
    /// <c>sha256:</c> prefix, or <see langword="null"/> when nothing is expected.
    /// </summary>
    public string WeightsSha256 { get; }

    /// <summary>The display name and the store name, for a log line or a test failure.</summary>
    /// <returns>The display name followed by the store name in brackets.</returns>
    public override string ToString()
    {
        return DisplayName + " (" + Name + ")";
    }

    /// <summary>
    /// Checks a digest and lower-cases it, so that two descriptors written with different casing compare
    /// the same way against a manifest.
    /// </summary>
    /// <param name="digest">The digest as it was supplied, which may be <see langword="null"/>.</param>
    /// <returns>The digest in lower case, or <see langword="null"/> when none was supplied.</returns>
    /// <exception cref="ArgumentException">The digest is not sixty-four hexadecimal characters.</exception>
    private static string NormalizeDigest(string digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        string trimmed = digest.Trim();
        if (trimmed.Length != Sha256HexLength)
        {
            throw new ArgumentException(
                "A weights digest is sixty-four hexadecimal characters with no 'sha256:' prefix; '"
                    + digest + "' is not.",
                nameof(digest));
        }
        foreach (char character in trimmed)
        {
            if (!Uri.IsHexDigit(character))
            {
                throw new ArgumentException(
                    "A weights digest is sixty-four hexadecimal characters with no 'sha256:' prefix; '"
                        + digest + "' is not.",
                    nameof(digest));
            }
        }

        return trimmed.ToLowerInvariant();
    }
}
