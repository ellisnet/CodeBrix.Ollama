using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager.Tests;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// The one store the export tests work in, and the models they export from. It is NOT a temporary
/// directory: an export test downloads about a gigabyte, and a second run of the suite must not pay for
/// that again, so the store lives in the test-model cache beside the model files the other suites keep
/// there and nothing here ever deletes it.
/// </summary>
/// <remarks>
/// A source model is pulled once, on the first test that asks for it, and afterwards it is simply
/// there. The derived bundles the tests write are deleted at the end of each test, so a run neither
/// leaves derived artifacts behind nor re-downloads what it derived them from.
/// </remarks>
public static class ExportTestStore
{
    /// <summary>The folder inside the cache the store itself lives in.</summary>
    public const string StoreFolderName = "export-store";

    /// <summary>The application folder the cache sits under when no variable names one.</summary>
    private const string CacheFolderName = "CodeBrix.Ollama";

    /// <summary>The cache folder the models are kept in when no variable names one.</summary>
    private const string ModelsFolderName = "test-models";

    /// <summary>
    /// Where the export tests keep their store: the folder
    /// <see cref="TestGates.TestModelDirectory"/> names, or the same default the other suites use.
    /// </summary>
    /// <returns>An absolute directory path. It is created when it is first used.</returns>
    public static string ResolveDirectory()
    {
        string fromEnvironment = Environment.GetEnvironmentVariable(TestGates.TestModelDirectory);
        string cache = string.IsNullOrWhiteSpace(fromEnvironment)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CacheFolderName,
                ModelsFolderName)
            : Path.GetFullPath(fromEnvironment.Trim());

        return Path.Combine(cache, StoreFolderName);
    }

    /// <summary>
    /// Opens the store the export tests work in.
    /// </summary>
    /// <returns>The store. The caller disposes it.</returns>
    public static ModelStore Open()
    {
        string directory = ResolveDirectory();
        Directory.CreateDirectory(directory);
        return new ModelStore(new ModelStoreOptions { StoreDirectory = directory });
    }

    /// <summary>
    /// Makes sure one of the pinned music models is in the store, downloading it only if it is not.
    /// </summary>
    /// <param name="store">The store to pull into.</param>
    /// <param name="model">The definition to pull.</param>
    /// <param name="cancellationToken">A token that cancels the pull.</param>
    /// <returns>The name the model is stored under.</returns>
    public static async Task<string> EnsureAsync(
        ModelStore store, MusicModel model, CancellationToken cancellationToken)
    {
        string name = model.Definition.Name;
        if (await store.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            return name;
        }

        await foreach (PullProgress report in store.PullAsync(
            name, model.Definition.ToPullOptions(), cancellationToken).ConfigureAwait(false))
        {
            _ = report;
        }

        return name;
    }

    /// <summary>
    /// Removes a derived bundle if it is there, so that a test can be run again.
    /// </summary>
    /// <param name="store">The store to remove it from.</param>
    /// <param name="name">The derived bundle's name.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>A task that completes when the bundle is gone.</returns>
    public static async Task RemoveAsync(ModelStore store, string name, CancellationToken cancellationToken)
    {
        if (await store.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            await store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The total size on disk of the files a bundle holds.
    /// </summary>
    /// <param name="files">The resolved files.</param>
    /// <returns>The total size in bytes.</returns>
    public static long TotalBytes(IReadOnlyList<ResolvedFile> files)
    {
        long total = 0;
        foreach (ResolvedFile file in files)
        {
            total += file.Size;
        }
        return total;
    }
}
