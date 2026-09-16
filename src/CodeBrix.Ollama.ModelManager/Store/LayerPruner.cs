using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama server/images.go;

/// <summary>
/// Removes blobs no manifest names any more. Blobs are shared between models, so a blob may only be
/// deleted once every manifest in the store has been checked; corrupt manifests are skipped rather
/// than treated as referencing nothing, which is the conservative reading Ollama uses too.
/// </summary>
internal static class LayerPruner
{
    /// <summary>
    /// Deletes the blobs among <paramref name="candidateDigests"/> that no manifest in the store
    /// references, either as a content layer or as its config layer.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="candidateDigests">The digests that may be deleted.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>
    /// The digests that were deleted. A candidate whose blob file was already gone is reported as
    /// deleted as well, matching Ollama, because the outcome the caller cares about is the same: the
    /// store no longer holds that blob.
    /// </returns>
    public static async Task<IReadOnlyList<string>> RemoveUnreferencedAsync(
        ModelStorePaths paths,
        IEnumerable<string> candidateDigests,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (candidateDigests == null)
        {
            throw new ArgumentNullException(nameof(candidateDigests));
        }

        // Keyed by the canonical colon-and-lowercase spelling so that a candidate written one way and
        // a manifest written the other still match; the caller's own spelling is what is reported back.
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string digest in candidateDigests)
        {
            if (!string.IsNullOrEmpty(digest))
            {
                candidates[Normalize(digest)] = digest;
            }
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        // Corrupt manifests are ignored so that they cannot block the deletion of blobs that were
        // just orphaned, which is what Ollama's deleteUnusedLayers does.
        IReadOnlyList<StoredManifest> manifests =
            await ManifestFiles.EnumerateAsync(paths, true, cancellationToken).ConfigureAwait(false);

        foreach (StoredManifest stored in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ModelManifest manifest = stored.Manifest;
            if (manifest.Layers != null)
            {
                foreach (ModelLayer layer in manifest.Layers)
                {
                    if (layer != null && !string.IsNullOrEmpty(layer.Digest))
                    {
                        candidates.Remove(Normalize(layer.Digest));
                    }
                }
            }

            if (manifest.Config != null && !string.IsNullOrEmpty(manifest.Config.Digest))
            {
                candidates.Remove(Normalize(manifest.Config.Digest));
            }
        }

        var deleted = new List<string>();
        foreach (string digest in candidates.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string blobPath;
            try
            {
                blobPath = paths.GetBlobPath(digest);
            }
            catch (ArgumentException)
            {
                continue;
            }

            try
            {
                File.Delete(blobPath);
            }
            catch (DirectoryNotFoundException)
            {
                // Nothing to delete; the blob is not in the store.
            }

            deleted.Add(digest);
        }

        return deleted;
    }

    /// <summary>
    /// Walks the blobs directory and deletes everything that is both older than
    /// <paramref name="gracePeriod"/> and unreferenced. The grace period keeps a blob that was just
    /// written, but whose manifest has not been written yet, from being collected mid-pull.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="gracePeriod">How recently a file must have been written to be left alone.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The digests of the blobs that were deleted.</returns>
    /// <remarks>
    /// Ollama deletes every file in the blobs directory whose name is not a digest. This library does
    /// not, because the same directory may belong to a real Ollama install whose own partial-download
    /// sidecars would then disappear underneath it. Only leftovers this library itself writes, named
    /// by <see cref="ModelStorePaths.GetPartialDataPath"/> and
    /// <see cref="ModelStorePaths.GetPartialStatePath"/>, are cleaned up here, and they are not
    /// reported in the returned list because they are not blobs.
    /// </remarks>
    public static async Task<IReadOnlyList<string>> PruneAllAsync(
        ModelStorePaths paths,
        TimeSpan gracePeriod,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        if (!Directory.Exists(paths.BlobsDirectory))
        {
            return Array.Empty<string>();
        }

        DateTime cutoff = DateTime.UtcNow - gracePeriod;
        var candidates = new List<string>();

        foreach (string file in Directory.EnumerateFiles(paths.BlobsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTime lastWrite;
            try
            {
                lastWrite = File.GetLastWriteTimeUtc(file);
            }
            catch (IOException)
            {
                continue;
            }

            if (lastWrite > cutoff)
            {
                continue;
            }

            string fileName = Path.GetFileName(file);
            string digest = Sha256Digest.ToDigest(fileName);
            if (Sha256Digest.IsValid(digest))
            {
                candidates.Add(digest);
                continue;
            }

            if (ModelStorePaths.IsPartialSidecarFileName(fileName))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // A sidecar that cannot be deleted now is picked up by the next prune.
                }
            }
        }

        return await RemoveUnreferencedAsync(paths, candidates, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The canonical spelling of a digest for comparison: a colon separator and lowercase hexadecimal.
    /// </summary>
    /// <param name="digest">The digest, in either spelling and either case.</param>
    /// <returns>The canonical spelling.</returns>
    private static string Normalize(string digest)
    {
        return Sha256Digest.ToDigest(digest).ToLowerInvariant();
    }
}
