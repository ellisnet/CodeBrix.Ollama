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
    /// Downloads a model into the store from where its publisher actually keeps it, which need not be a
    /// registry that speaks Ollama's protocol: a Hugging Face file repository or a plain list of HTTPS
    /// addresses, a public storage bucket among them. Such a model is a BUNDLE - a set of the
    /// publisher's own files rather than a GGUF weights file - and it is stored in the same
    /// content-addressed blob and manifest layout as every other model, so listing, describing,
    /// resolving, copying, deleting and pruning work on it unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bundle pull is always asked for. <see cref="PullOptions.Source"/> of
    /// <see cref="PullSource.Registry"/>, and <paramref name="options"/> of <see langword="null"/>, both
    /// mean exactly what <see cref="PullAsync(string, CancellationToken)"/> means, and a name that the
    /// registry protocol cannot serve fails there as it always has rather than quietly taking another
    /// route.
    /// </para>
    /// <para>
    /// One layer per file is written, each carrying the publisher's relative path, and the config layer
    /// records where the files came from, the revision the listing resolved to and whatever the source
    /// states about the licence. A file already in the store costs no request. A file the source states
    /// a SHA-256 for is verified against it; a file it states only an MD5 for is verified against that;
    /// and the store computes the SHA-256 of every file in any case, so a second pull verifies against
    /// the first.
    /// </para>
    /// </remarks>
    /// <param name="name">The name the model is stored under.</param>
    /// <param name="options">
    /// Where the files come from, which of them are wanted and how strict the verification is.
    /// <see langword="null"/> means a plain registry pull.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation; partial downloads are kept for resumption.</param>
    /// <returns>
    /// A stream of progress reports: "listing &lt;repository&gt;", then "pulling &lt;path&gt;" for each
    /// file with its byte counts, then "verifying sha256 digest", "writing manifest" and "success".
    /// </returns>
    /// <exception cref="ArgumentException">
    /// A Hugging Face pull was asked for under a name that is not a Hugging Face name and
    /// <see cref="PullOptions.Repository"/> was not set, or a file-list pull was asked for with no files.
    /// </exception>
    /// <exception cref="ModelManagerException">
    /// <see cref="PullOptions.RequireHashes"/> is set and the source states no hash for a file, or the
    /// source listed nothing to pull.
    /// </exception>
    /// <exception cref="RegistryException">The source refused or failed a request.</exception>
    /// <exception cref="DigestMismatchException">A downloaded file did not match the hash its source stated.</exception>
    IAsyncEnumerable<PullProgress> PullAsync(
        string name, PullOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a folder already on disk as a bundle: every file under it is hashed, stored as a blob and
    /// recorded as a layer carrying its path relative to the folder. This is the route for anything a
    /// publisher keeps behind a sign-in - fetch it however it has to be fetched, then hand the folder
    /// over - and for any set of files a consumer produced itself.
    /// </summary>
    /// <remarks>
    /// The conventional name for an imported bundle uses the host <c>local</c>, as in
    /// <c>local/&lt;namespace&gt;/&lt;model&gt;:&lt;tag&gt;</c>, which the name grammar accepts like any
    /// other host and which no registry can ever be confused with. Nothing enforces it.
    /// </remarks>
    /// <param name="name">The name the bundle is stored under.</param>
    /// <param name="directory">The folder to import. It is walked to its full depth.</param>
    /// <param name="options">
    /// Which files are wanted, whether they are linked rather than copied and what is known about the
    /// licence. <see langword="null"/> copies every file and states no licence.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the manifest has been written.</returns>
    /// <exception cref="ArgumentException">The name or the directory is missing.</exception>
    /// <exception cref="ModelManagerException">
    /// The directory does not exist, or holds no file the filter keeps.
    /// </exception>
    Task ImportBundleAsync(
        string name, string directory, ImportOptions options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a bundle's files out as the directory tree its publisher wrote, so that they can be worked
    /// with as ordinary files. The store keeps them content addressed, under names that say what they
    /// are and not what they are called; this puts the publisher's names back.
    /// </summary>
    /// <param name="name">The model name. It must name a bundle.</param>
    /// <param name="targetDirectory">The directory the tree is written under. It is created if missing.</param>
    /// <param name="options">
    /// How each file is put in place and what happens to a file already there. <see langword="null"/>
    /// hard-links where it can, copies where it cannot, and refuses to replace anything.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The absolute paths that were written, in manifest order.</returns>
    /// <exception cref="ArgumentException">The name or the target directory is missing.</exception>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    /// <exception cref="InvalidOperationException">
    /// The model is not a bundle: it carries no publisher file tree, which is the case for every model
    /// pulled from an Ollama-protocol registry.
    /// </exception>
    /// <exception cref="ModelManagerException">
    /// A file is already at a target path and <see cref="MaterializeOptions.Overwrite"/> is not set, a
    /// layer names a blob that is not in the store or a path outside the target directory, or a symbolic
    /// link was asked for and could not be made.
    /// </exception>
    Task<IReadOnlyList<string>> MaterializeAsync(
        string name,
        string targetDirectory,
        MaterializeOptions options = null,
        CancellationToken cancellationToken = default);

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
