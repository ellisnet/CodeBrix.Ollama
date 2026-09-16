using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A local store of models laid out exactly as Ollama lays out <c>~/.ollama/models</c>: blobs under
/// <c>blobs/sha256-&lt;hex&gt;</c> and manifests under
/// <c>manifests/&lt;host&gt;/&lt;namespace&gt;/&lt;model&gt;/&lt;tag&gt;</c>. Models are pulled from any
/// registry that speaks Ollama's manifest-and-blob protocol (registry.ollama.ai and hf.co among them),
/// and a name is resolved to the GGUF files an in-process runner loads.
/// </summary>
/// <remarks>
/// Every model name argument is a string in Ollama's name syntax
/// (<c>[scheme://][host/][namespace/]model[:tag]</c>) and is completed with the defaults from
/// <see cref="ModelStoreOptions"/>. See <see cref="ModelName.Parse(string)"/>.
/// </remarks>
public interface IModelStore
{
    /// <summary>
    /// The absolute path of the store directory.
    /// </summary>
    string StoreDirectory { get; }

    /// <summary>
    /// Downloads a model from its registry into the store, reporting progress as it goes. Layers already
    /// present are not downloaded again; an interrupted download resumes where it stopped. The manifest
    /// is written last, so a model is either fully present or absent. Layers that the previous manifest
    /// for the same name referenced and no other manifest references are removed afterwards.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="cancellationToken">A token to cancel the operation; partial downloads are kept for resumption.</param>
    /// <returns>A stream of progress reports ending with a "success" status.</returns>
    /// <exception cref="ModelNotFoundException">The registry has no such model or tag.</exception>
    /// <exception cref="RegistryException">The registry refused or failed a request.</exception>
    /// <exception cref="DigestMismatchException">A downloaded layer did not hash to its digest.</exception>
    IAsyncEnumerable<PullProgress> PullAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every model in the store, most recently modified first.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One summary per manifest. Manifests that cannot be read are skipped.</returns>
    Task<IReadOnlyList<ModelSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether the store holds a manifest for the name.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the manifest file exists.</returns>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes a model in full: manifest, config, decoded text and JSON layers, GGUF metadata and
    /// inferred capabilities.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The model description.</returns>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    Task<ModelInfo> ShowAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a model name to the files on disk an in-process runner loads, plus its template, system
    /// prompt, parameters, licenses and preset messages.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolved paths and layers.</returns>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    Task<ResolvedModel> ResolveAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives an existing model a second name. Only the manifest is copied; every blob is shared.
    /// </summary>
    /// <param name="sourceName">The existing model name.</param>
    /// <param name="destinationName">The new model name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the manifest has been written.</returns>
    /// <exception cref="ModelNotFoundException">The store has no such source model.</exception>
    Task CopyAsync(string sourceName, string destinationName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a model's manifest, then removes every blob that no remaining manifest references.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the manifest and unreferenced blobs are gone.</returns>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a model from a Modelfile. FROM names either a model already in the store (whose layers
    /// are inherited) or a GGUF file on disk (which is hashed and imported as a blob); nothing is
    /// downloaded. TEMPLATE, SYSTEM, PARAMETER, LICENSE, MESSAGE and ADAPTER lines become layers; a
    /// template, system or messages line replaces the inherited one, parameters merge over inherited
    /// ones, and licenses append.
    /// </summary>
    /// <param name="name">The name to give the new model.</param>
    /// <param name="modelfile">The parsed Modelfile.</param>
    /// <param name="options">Options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the manifest has been written.</returns>
    /// <exception cref="ModelNotFoundException">FROM names a model that is not in the store and not a file.</exception>
    /// <exception cref="GgufFormatException">FROM or ADAPTER names a file that is not a usable GGUF.</exception>
    Task CreateAsync(string name, Modelfile modelfile, CreateOptions options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every blob in the store that no manifest references, and every download sidecar this
    /// library left behind, provided the file is older than <paramref name="gracePeriod"/>. Files
    /// younger than that may belong to a pull still in progress and are left alone. Files this library
    /// does not recognise are never touched.
    /// </summary>
    /// <param name="gracePeriod">How old a file must be before it is eligible; Ollama uses one hour.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The digests of the blobs that were removed.</returns>
    Task<IReadOnlyList<string>> PruneAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default);
}
