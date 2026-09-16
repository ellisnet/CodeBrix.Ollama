using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama manifest/manifest.go;

/// <summary>
/// Reading, writing, listing and deleting the manifest files of a store. A manifest lives at
/// <c>manifests/&lt;host&gt;/&lt;namespace&gt;/&lt;model&gt;/&lt;tag&gt;</c>, so listing the store means
/// looking at exactly four levels below the manifests directory, which is what Ollama's
/// <c>Manifests()</c> does with its <c>*/*/*/*</c> glob.
/// </summary>
internal static class ManifestFiles
{
    /// <summary>
    /// The prefix given to the temporary file a manifest is written through. It starts with a dot so
    /// that it can never parse as a tag and therefore never shows up as a model while a write is in
    /// flight.
    /// </summary>
    private const string TempFilePrefix = ".codebrix-tmp-";

    /// <summary>
    /// Reads the manifest of a model name.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The stored manifest, or <see langword="null"/> when no such manifest file exists.</returns>
    /// <exception cref="ModelManagerException">The file exists but does not hold a valid manifest.</exception>
    public static async Task<StoredManifest> ReadAsync(
        ModelStorePaths paths,
        ModelName name,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        string path = paths.GetManifestPath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] rawBytes;
        DateTime modifiedAt;
        try
        {
            rawBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            modifiedAt = File.GetLastWriteTimeUtc(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        ModelManifest manifest;
        try
        {
            manifest = ModelManagerJson.Deserialize<ModelManifest>(rawBytes);
        }
        catch (JsonException exception)
        {
            throw new ModelManagerException(
                $"manifest {path} is not valid JSON: {exception.Message}",
                exception);
        }

        if (manifest == null)
        {
            throw new ModelManagerException($"manifest {path} is not valid JSON: the document is empty.");
        }

        string digest = Sha256Digest.Compute(rawBytes).Substring(Sha256Digest.Prefix.Length);
        return new StoredManifest(
            name,
            path,
            manifest,
            digest,
            new DateTimeOffset(modifiedAt, TimeSpan.Zero),
            rawBytes);
    }

    /// <summary>
    /// Reports whether a manifest file exists for a model name.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="cancellationToken">A token that cancels the check.</param>
    /// <returns><see langword="true"/> when the manifest file exists.</returns>
    public static Task<bool> ExistsAsync(
        ModelStorePaths paths,
        ModelName name,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(paths.GetManifestPath(name)));
    }

    /// <summary>
    /// Writes the exact bytes of a manifest, creating the directories it needs. The bytes go to a
    /// temporary file in the same directory and are then moved into place, so a concurrent reader
    /// sees either the old manifest or the new one and never a half-written file.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="rawBytes">The bytes to write.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the manifest is in place.</returns>
    public static async Task WriteAsync(
        ModelStorePaths paths,
        ModelName name,
        byte[] rawBytes,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        string path = paths.GetManifestPath(name);
        string directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, TempFilePrefix + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(tempPath, rawBytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Serializes a manifest the way Ollama's JSON encoder does, with schema version 2 and the
    /// manifest media type, and writes it.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="manifest">The manifest to write.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the manifest is in place.</returns>
    public static Task WriteAsync(
        ModelStorePaths paths,
        ModelName name,
        ModelManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var document = new ModelManifest
        {
            SchemaVersion = 2,
            MediaType = MediaTypes.Manifest,
            Config = manifest.Config,
            Layers = manifest.Layers ?? new List<ModelLayer>()
        };
        return WriteAsync(paths, name, ModelManagerJson.SerializeLikeGo(document), cancellationToken);
    }

    /// <summary>
    /// Lists every manifest in the store. Only files exactly four levels below the manifests directory
    /// are considered, matching Ollama's glob; anything shallower or deeper is ignored.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="continueOnError">
    /// When <see langword="true"/>, a path that does not parse as a model name and a file that does not
    /// hold a valid manifest are skipped; when <see langword="false"/>, either one throws.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the walk.</param>
    /// <returns>The manifests found, in the order the directory walk produced them.</returns>
    /// <exception cref="ModelManagerException">
    /// A bad name or an unreadable manifest was found and <paramref name="continueOnError"/> is
    /// <see langword="false"/>.
    /// </exception>
    public static async Task<IReadOnlyList<StoredManifest>> EnumerateAsync(
        ModelStorePaths paths,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        var results = new List<StoredManifest>();
        if (!Directory.Exists(paths.ManifestsDirectory))
        {
            return results;
        }

        foreach (string hostDirectory in Directory.EnumerateDirectories(paths.ManifestsDirectory))
        {
            foreach (string namespaceDirectory in Directory.EnumerateDirectories(hostDirectory))
            {
                foreach (string modelDirectory in Directory.EnumerateDirectories(namespaceDirectory))
                {
                    foreach (string tagFile in Directory.EnumerateFiles(modelDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string relativePath = Path.GetRelativePath(paths.ManifestsDirectory, tagFile);
                        ModelName name = ModelName.ParseFromRelativePath(relativePath);
                        if (!name.IsFullyQualified)
                        {
                            if (!continueOnError)
                            {
                                throw new ModelManagerException(
                                    $"manifest path {relativePath} does not name a valid model.");
                            }
                            continue;
                        }

                        StoredManifest stored;
                        try
                        {
                            stored = await ReadAsync(paths, name, cancellationToken).ConfigureAwait(false);
                        }
                        catch (ModelManagerException)
                        {
                            if (!continueOnError)
                            {
                                throw;
                            }
                            continue;
                        }
                        catch (IOException)
                        {
                            if (!continueOnError)
                            {
                                throw;
                            }
                            continue;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            if (!continueOnError)
                            {
                                throw;
                            }
                            continue;
                        }

                        if (stored != null)
                        {
                            results.Add(stored);
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Deletes the manifest of a model name, then removes the directories the deletion left empty, up
    /// to but never including the manifests directory itself. A directory that is a symbolic link is
    /// left alone, as Ollama's <c>PruneDirectory</c> does.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>A task that completes when the manifest and any empty parents are gone.</returns>
    public static Task DeleteAsync(
        ModelStorePaths paths,
        ModelName name,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string path = paths.GetManifestPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        PruneEmptyParents(paths.ManifestsDirectory, Path.GetDirectoryName(path));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes empty directories from <paramref name="startDirectory"/> upwards, stopping at
    /// <paramref name="stopDirectory"/>, which is never removed.
    /// </summary>
    /// <param name="stopDirectory">The directory the walk stops below.</param>
    /// <param name="startDirectory">The deepest directory to consider.</param>
    private static void PruneEmptyParents(string stopDirectory, string startDirectory)
    {
        string stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stopDirectory));
        string current = startDirectory;

        while (!string.IsNullOrEmpty(current))
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
            if (string.Equals(full, stop, StringComparison.Ordinal))
            {
                return;
            }
            if (!full.StartsWith(stop + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return;
            }
            if (!Directory.Exists(full))
            {
                current = Path.GetDirectoryName(full);
                continue;
            }

            var info = new DirectoryInfo(full);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }
            using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(full).GetEnumerator())
            {
                if (entries.MoveNext())
                {
                    return;
                }
            }

            Directory.Delete(full);
            current = Path.GetDirectoryName(full);
        }
    }
}
