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
    /// Exports a bundle to ONNX and stores the result as a DERIVED BUNDLE - a bundle this library
    /// produced itself, which lists, shows, resolves, materializes and deletes like any other, and whose
    /// config records what made it and from what.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ExportRoute.Auto"/>, the default, reads the source and decides: a bundle that already
    /// ships <c>.onnx</c> files is PASSED THROUGH - its graphs and the files that go with them are
    /// registered under the new name without converting or copying anything, and NO PYTHON IS NEEDED OR
    /// STARTED; a checkpoint whose <c>config.json</c> names an architecture the ONNX Runtime GenAI model
    /// builder writes goes to <see cref="ExportRoute.GenAiBuilder"/>; anything else goes to
    /// <see cref="ExportRoute.Optimum"/>.
    /// </para>
    /// <para>
    /// Both converting routes need Python: the modules are checked before anything is laid out, the
    /// source bundle is written into a temporary folder under the system temporary directory, the
    /// publisher's own tool is run over it, and what it wrote is collected into the store and the
    /// temporary folder removed. On a machine whose temporary directory is in memory, point TMPDIR at a
    /// real file system first - a checkpoint is gigabytes.
    /// </para>
    /// </remarks>
    /// <param name="name">The model name. It must name a bundle.</param>
    /// <param name="options">
    /// Which route, at what precision, under what name, and whether an existing bundle of that name may
    /// be replaced. <see langword="null"/> means the automatic route at fp32 under the source name with
    /// its tag replaced by <c>onnx</c>, replacing nothing.
    /// </param>
    /// <param name="progress">
    /// Where the statuses go - "materializing source", "exporting", "collecting", "writing manifest",
    /// "success" - or <see langword="null"/> to report nothing.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation. It cancels the wait for the interpreter and the work around the
    /// export, never a conversion already running inside the tool.
    /// </param>
    /// <returns>The name of the derived bundle, the files it holds and what produced them.</returns>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    /// <exception cref="InvalidOperationException">
    /// The model is not a bundle, or <see cref="ExportRoute.PublisherOnnx"/> was named for a bundle that
    /// ships no <c>.onnx</c> file.
    /// </exception>
    /// <exception cref="PythonNotAvailableException">
    /// A converting route was chosen and there is no usable CPython.
    /// </exception>
    /// <exception cref="PythonModuleNotInstalledException">
    /// A converting route was chosen and one of the modules it needs is not installed.
    /// </exception>
    /// <exception cref="PythonScriptException">The tool refused the model, or failed while writing it.</exception>
    /// <exception cref="ModelManagerException">
    /// The output name is taken and <see cref="ExportOptions.Overwrite"/> is not set, or the tool wrote
    /// nothing.
    /// </exception>
    Task<ExportResult> ExportToOnnxAsync(
        string name,
        ExportOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the exported graphs a bundle holds SMALLER by quantizing their weights, and stores the
    /// result as a derived bundle - one this library produced itself, which lists, shows, resolves,
    /// materializes and deletes like any other, and whose config records what made it and from what.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A REDUCED MODEL IS AN APPROXIMATION OF THE ONE IT CAME FROM. Quantizing replaces floating-point
    /// weights with smaller integers and a scale, which is arithmetic that cannot be undone: the file is
    /// a fraction of the size and the numbers the model produces are close to, not the same as, what it
    /// produced before. Measure what it does before shipping it.
    /// </para>
    /// <para>
    /// Every <c>.onnx</c> file in the bundle is reduced unless <see cref="ReduceOptions.Files"/> names
    /// some, and everything else the bundle holds - configurations, tokenizers, a model card, a graph
    /// nobody asked to reduce - is carried through unchanged, so the derived bundle is a whole model and
    /// not a folder of graphs. The source is laid out in a temporary folder under the system temporary
    /// directory, the graphs are reduced into another, and what was written is collected into the store
    /// and the temporary folders removed. On a machine whose temporary directory is in memory, point
    /// TMPDIR at a real file system first.
    /// </para>
    /// <para>
    /// TWO ENGINES DO THE WORK. The managed engine is this library's own code and needs nothing installed:
    /// it covers <see cref="ReduceMode.WeightOnlyInt4"/> and <see cref="ReduceMode.WeightOnlyInt8"/>
    /// always, and <see cref="ReduceMode.DynamicInt8"/> on a graph that has already been prepared. The
    /// Python engine runs ONNX Runtime's own tools and needs <c>onnx</c> and <c>onnxruntime</c> installed
    /// in the CPython this process finds; it covers <see cref="ReduceMode.PreprocessOnly"/>, which is
    /// what prepares a graph, and dynamic quantization of a graph nobody has prepared. Modules are
    /// checked before any file is laid out, and only for the engine that is going to run.
    /// </para>
    /// </remarks>
    /// <param name="name">The model name. It must name a bundle that holds at least one <c>.onnx</c> file.</param>
    /// <param name="options">
    /// Which mode, with which block-wise settings, through which engine, over which files, under what
    /// name, and whether an existing bundle of that name may be replaced. <see langword="null"/> means
    /// dynamic INT8 over every graph, prepared first, under the source name with the mode's tag added.
    /// </param>
    /// <param name="progress">
    /// Where the statuses go - "materializing source", "reducing &lt;file&gt;", "collecting", "writing
    /// manifest", "success" - or <see langword="null"/> to report nothing.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation. It cancels the wait for the interpreter and the work between
    /// graphs, never a quantization already running inside the tool.
    /// </param>
    /// <returns>
    /// The name of the derived bundle, the files it holds, the engine and mode that ran, and how much
    /// smaller the graphs became.
    /// </returns>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    /// <exception cref="InvalidOperationException">The model is not a bundle.</exception>
    /// <exception cref="NotSupportedException">
    /// <see cref="ReduceEngine.Managed"/> was named for <see cref="ReduceMode.PreprocessOnly"/>, which it
    /// does not cover, or for a graph holding something it does not quantize.
    /// </exception>
    /// <exception cref="PythonNotAvailableException">There is no usable CPython.</exception>
    /// <exception cref="PythonModuleNotInstalledException">
    /// One of the modules the engine needs is not installed.
    /// </exception>
    /// <exception cref="PythonScriptException">The tool refused a graph, or failed while writing one.</exception>
    /// <exception cref="ModelManagerException">
    /// The bundle holds no <c>.onnx</c> file, <see cref="ReduceOptions.Files"/> names one it does not
    /// hold, a graph is too large to prepare, the output name is taken and
    /// <see cref="ReduceOptions.Overwrite"/> is not set, or dynamic quantization was asked for on an
    /// unprepared graph with no CPython to prepare it in.
    /// </exception>
    Task<ReduceResult> ReduceOnnxAsync(
        string name,
        ReduceOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a transformers checkpoint held as a bundle into an ordinary GGUF model in the store - one
    /// that lists, shows, resolves and deletes like any other GGUF model, and whose file an in-process
    /// runner loads through <see cref="ResolveAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOTHING IS INSTALLED AND NO PYTHON IS STARTED. The conversion is this library's own managed code: it
    /// reads the checkpoint's weights - safetensors or a PyTorch zip pickle - maps every tensor on to the
    /// name the inference engine expects, reads the tokenizer out of the files the publisher shipped, and
    /// writes the GGUF file the engine's own converter would have written.
    /// </para>
    /// <para>
    /// The source bundle is laid out in a temporary folder under the system temporary directory, the GGUF
    /// file is written beside it, the result is stored as a model, and the whole folder is removed however
    /// the conversion ends. On a machine whose temporary directory is in memory, point TMPDIR at a real
    /// file system first - a checkpoint is gigabytes.
    /// </para>
    /// <para>
    /// The result records its provenance the way every derived artifact of this library does: which model
    /// it came from, that this library made it, and what it was asked for. The source's licence record is
    /// carried over unchanged, and a licence TEXT the source ships becomes a licence layer.
    /// </para>
    /// </remarks>
    /// <param name="name">The model name. It must name a bundle holding a transformers checkpoint.</param>
    /// <param name="options">
    /// Which numeric type to write, which architecture to read the checkpoint as, what to call the result,
    /// whether a model of that name may be replaced, and what the checkpoint's files do not say about its
    /// special tokens. <see langword="null"/> keeps the checkpoint's own numeric type, reads the
    /// architecture from the configuration and stores the result under the source name with the tag
    /// <c>gguf</c>, replacing nothing.
    /// </param>
    /// <param name="progress">
    /// Where the statuses go - "materializing source", "reading checkpoint", "writing gguf", "creating
    /// model", "success" - or <see langword="null"/> to report nothing.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The name the model was stored under, how the checkpoint was read, how many tensors it wrote, how
    /// large the two files are and which numeric type was written.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <see cref="ConvertOptions.AddedSpecialTokens"/> names a token the checkpoint's vocabulary does not
    /// hold.
    /// </exception>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    /// <exception cref="InvalidOperationException">
    /// The model is not a bundle, or it holds a GGUF file or an exported ONNX graph rather than a
    /// checkpoint.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The checkpoint's architecture, tokenizer, rotary scaling or layout is one this version does not
    /// convert; the message names what the checkpoint's own files said.
    /// </exception>
    /// <exception cref="CheckpointFormatException">
    /// The checkpoint has no configuration, or one of its containers is malformed.
    /// </exception>
    /// <exception cref="PickleRefusedException">
    /// The checkpoint is a PyTorch pickle holding a construct the restricted reader will not interpret.
    /// </exception>
    /// <exception cref="ModelManagerException">
    /// The output name is taken and <see cref="ConvertOptions.Overwrite"/> is not set.
    /// </exception>
    Task<ConvertResult> ConvertToGgufAsync(
        string name,
        ConvertOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quantizes a stored GGUF model with a quantizer THE CALLER SUPPLIES, and puts the smaller file away as
    /// an ordinary GGUF model in the store - one that lists, shows, resolves and deletes like any other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE QUANTIZER IS NOT THIS LIBRARY'S. Quantizing weights is an inference engine's work, and this
    /// library depends on no inference engine: it has none, it loads none, and the package that carries one
    /// is a package this one does not reference. <see cref="QuantizeGgufOptions.Quantizer"/> is therefore
    /// required, and what the store contributes is everything around it - finding the file, naming the
    /// result, carrying the source's template, parameters and licence over, recording where it came from,
    /// and cleaning up whatever happens. <c>CodeBrix.Ollama.ModelRunner.QuantizeAsync</c> is a quantizer that
    /// fits this shape exactly; so does a command-line tool a consumer starts itself.
    /// </para>
    /// <para>
    /// The stored file is handed to the quantizer WHERE IT LIES, so nothing is copied, and the quantized file
    /// is written into a temporary folder under the system temporary directory, taken into the store, and the
    /// folder removed however the work ends. On a machine whose temporary directory is in memory, point
    /// TMPDIR at a real file system first - a model is gigabytes.
    /// </para>
    /// <para>
    /// The result records its provenance the way every derived artifact of this library does: which model it
    /// came from, what the caller says did the work, and which type was asked for. The source's licence
    /// record and licence texts are carried over, and so are its template, system prompt, parameters and
    /// stored messages - quantizing changes the weights and nothing else about how a model is used.
    /// </para>
    /// </remarks>
    /// <param name="name">The model name. It must name a GGUF model held in a single file.</param>
    /// <param name="options">
    /// The quantizer, what the type is called, what to call the result, whether a model of that name may be
    /// replaced, and what to record as having done the work. It is required, and so are its
    /// <see cref="QuantizeGgufOptions.Quantizer"/> and <see cref="QuantizeGgufOptions.Type"/>.
    /// </param>
    /// <param name="progress">
    /// Where the statuses go - "quantizing", "creating model", "success" - or <see langword="null"/> to
    /// report nothing.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The name the model was stored under, the model it came from, the type, the two sizes and the tool the
    /// caller named.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The options carry no quantizer, or a type that cannot be part of a model name.
    /// </exception>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    /// <exception cref="InvalidOperationException">
    /// The model is a publisher file tree rather than a GGUF model, or carries no weights at all.
    /// </exception>
    /// <exception cref="NotSupportedException">The model's weights are split over several files.</exception>
    /// <exception cref="ModelManagerException">
    /// The quantizer wrote no file, or the output name is taken and
    /// <see cref="QuantizeGgufOptions.Overwrite"/> is not set.
    /// </exception>
    Task<QuantizeGgufResult> QuantizeGgufAsync(
        string name,
        QuantizeGgufOptions options,
        IProgress<PullProgress> progress = null,
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
