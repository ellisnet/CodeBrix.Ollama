using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelManager.Tests;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The store these tests work in, and the checkpoints they convert. It is NOT a temporary directory: a source
/// checkpoint is hundreds of megabytes, and a second run of the suite must not pay for it again, so the store
/// is the same one in the test-model cache that the export tests use and nothing here ever deletes it.
/// </summary>
/// <remarks>
/// A source model is pulled once, on the first test that asks for it, and afterwards it is simply there. The
/// models these tests convert are removed when each test ends, so a run neither leaves converted models behind
/// nor re-downloads what it converted them from.
/// </remarks>
public static class EndToEndStore
{
    /// <summary>The folder inside the cache the store itself lives in.</summary>
    public const string StoreFolderName = "export-store";

    /// <summary>The application folder the cache sits under when no variable names one.</summary>
    private const string CacheFolderName = "CodeBrix.Ollama";

    /// <summary>The cache folder the models are kept in when no variable names one.</summary>
    private const string ModelsFolderName = "test-models";

    /// <summary>
    /// Where these tests keep their store: the folder <see cref="TestGates.TestModelDirectory"/> names, or the
    /// same default the other suites use.
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
    /// Opens the store these tests work in.
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
    /// Makes sure a REDUCED bundle is in the store, reducing the source only if it is not already there.
    /// </summary>
    /// <remarks>
    /// A derived bundle is KEPT between runs, unlike the ones the conversion tests make, and the reason is
    /// cost: reducing the larger of the two decoder graphs takes minutes, the dynamic mode needs an
    /// interpreter as well, and the answer is the same every time - the reduction is arithmetic over the same
    /// weights with the same settings. Keeping it turns a several-minute suite into a several-second one and
    /// costs a few hundred megabytes in the same cache the source models already live in. Delete the derived
    /// model to have it made again.
    /// </remarks>
    /// <param name="store">The store to reduce in.</param>
    /// <param name="source">The name of the bundle to reduce.</param>
    /// <param name="mode">Which reduction.</param>
    /// <param name="name">The name to store the result under.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The name the reduced bundle is stored under.</returns>
    public static async Task<string> EnsureReducedAsync(
        ModelStore store, string source, ReduceMode mode, string name, CancellationToken cancellationToken)
    {
        if (await store.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            return name;
        }

        ReduceResult result = await store.ReduceOnnxAsync(
            source,
            new ReduceOptions { Mode = mode, OutputName = name },
            null,
            cancellationToken).ConfigureAwait(false);
        return result.Name;
    }

    /// <summary>
    /// Makes sure a bundle EXPORTED to ONNX by the model builder is in the store, exporting it only if it is
    /// not already there.
    /// </summary>
    /// <remarks>
    /// It is kept between runs for the reason <see cref="EnsureReducedAsync"/> is: an export runs a
    /// publisher's own tool over a checkpoint of several hundred megabytes and takes minutes.
    /// </remarks>
    /// <param name="store">The store to export in.</param>
    /// <param name="source">The name of the checkpoint bundle.</param>
    /// <param name="precision">The precision to ask the builder for.</param>
    /// <param name="name">The name to store the result under.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The name the exported bundle is stored under.</returns>
    public static async Task<string> EnsureExportedAsync(
        ModelStore store, string source, string precision, string name, CancellationToken cancellationToken)
    {
        if (await store.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            return name;
        }

        ExportResult result = await store.ExportToOnnxAsync(
            source,
            new ExportOptions
            {
                Route = ExportRoute.GenAiBuilder,
                Precision = precision,
                OutputName = name,

                // The MuPT family ships its tokenizer as Python of its own, and the builder refuses to read
                // the checkpoint without being told it may import that file.
                AllowRemoteCode = true,
            },
            null,
            cancellationToken).ConfigureAwait(false);
        return result.Name;
    }

    /// <summary>
    /// Removes a converted model if it is there, so that a test can be run again.
    /// </summary>
    /// <param name="store">The store to remove it from.</param>
    /// <param name="name">The converted model's name.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>A task that completes when the model is gone.</returns>
    public static async Task RemoveAsync(ModelStore store, string name, CancellationToken cancellationToken)
    {
        if (await store.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            await store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The name a model is stored under, which is the name a converted model records having come from: a
    /// definition name may leave the tag out, and the store never does.
    /// </summary>
    /// <param name="name">The name as the definition writes it.</param>
    /// <returns>The stored name.</returns>
    public static string StoredName(string name) => ModelName.Parse(name).DisplayShortest();
}
