using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama server/images.go, server/create.go, server/model.go and x/create/manifest.go;

/// <summary>
/// The default <see cref="IModelStore"/>: a local model store in Ollama's on-disk layout, fed from any
/// registry that speaks Ollama's manifest-and-blob protocol.
/// </summary>
public sealed class ModelStore : IModelStore, IDisposable
{
    /// <summary>What a pull of a plain list of addresses calls the thing it is listing.</summary>
    private const string FileListLabel = "file list";

    /// <summary>The value the config's <c>source</c> property carries for a Hugging Face pull.</summary>
    private const string HubSource = "hf.co";

    /// <summary>The value the config's <c>source</c> property carries for a pull of a list of addresses.</summary>
    private const string UrlSource = "url";

    /// <summary>The value the config's <c>source</c> property carries for an imported folder.</summary>
    private const string LocalSource = "local";

    /// <summary>The value the config's <c>source</c> property carries for a bundle this library made.</summary>
    private const string DerivedSource = "derived";

    private static readonly IReadOnlyList<GgufMetadata> NoMetadata = Array.Empty<GgufMetadata>();
    private static readonly IReadOnlyList<ModelMessage> NoMessages = Array.Empty<ModelMessage>();

    private readonly ModelStoreOptions _options;
    private readonly ModelStorePaths _paths;
    private readonly object _sync = new object();
    private RegistryClient _registryClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a store.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> for Ollama-compatible defaults.</param>
    public ModelStore(ModelStoreOptions options = null)
    {
        _options = options ?? new ModelStoreOptions();
        StoreDirectory = Path.GetFullPath(_options.StoreDirectory ?? ModelStoreOptions.ResolveDefaultStoreDirectory());
        _paths = new ModelStorePaths(StoreDirectory);
    }

    /// <inheritdoc />
    public string StoreDirectory { get; }

    /// <inheritdoc />
    public async IAsyncEnumerable<PullProgress> PullAsync(
        string name,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));

        await foreach (PullProgress progress in StreamProgressAsync(
            (writer, token) => RunPullAsync(parsed, writer, token), cancellationToken).ConfigureAwait(false))
        {
            yield return progress;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PullProgress> PullAsync(
        string name,
        PullOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        PullOptions effective = options ?? PullOptions.ForRegistry();

        // A pull with no source of its own is the pull this library has always done, down to the same
        // code path: a bundle pull is something a caller asks for, never something it falls into.
        Func<ChannelWriter<PullProgress>, CancellationToken, Task> work;
        if (effective.Source == PullSource.Registry)
        {
            work = (writer, token) => RunPullAsync(parsed, writer, token);
        }
        else
        {
            work = (writer, token) => RunBundlePullAsync(parsed, effective, writer, token);
        }

        await foreach (PullProgress progress in StreamProgressAsync(work, cancellationToken).ConfigureAwait(false))
        {
            yield return progress;
        }
    }

    /// <summary>
    /// Runs one pull and hands its progress reports to the caller as they are made.
    /// </summary>
    /// <param name="work">The pull, which writes its reports into the channel it is given.</param>
    /// <param name="cancellationToken">A token that cancels the pull.</param>
    /// <returns>The reports, in the order they were made.</returns>
    private async IAsyncEnumerable<PullProgress> StreamProgressAsync(
        Func<ChannelWriter<PullProgress>, CancellationToken, Task> work,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<PullProgress>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        // The download runs as its own task and hands its progress reports to the iterator through the
        // channel, because the registry client reports progress through a callback and an iterator
        // cannot yield from one. Whatever the download throws is captured and rethrown to the consumer
        // once the reports it did manage to make have been yielded.
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception failure = null;
        Task download = null;
        try
        {
            CancellationToken downloadToken = linkedCancellation.Token;
            download = Task.Run(
                async () =>
                {
                    try
                    {
                        await work(channel.Writer, downloadToken).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                },
                CancellationToken.None);

            await foreach (PullProgress progress in channel.Reader
                .ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return progress;
            }

            await download.ConfigureAwait(false);
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            linkedCancellation.Cancel();
            if (download != null)
            {
                await download.ConfigureAwait(false);
            }
            linkedCancellation.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        IReadOnlyList<StoredManifest> stored = await ManifestFiles
            .EnumerateAsync(_paths, true, cancellationToken).ConfigureAwait(false);

        var summaries = new List<ModelSummary>(stored.Count);
        foreach (StoredManifest manifest in stored)
        {
            summaries.Add(new ModelSummary
            {
                Name = manifest.Name,
                DisplayName = manifest.Name.DisplayShortest(),
                Digest = manifest.Digest,
                Size = manifest.Manifest.GetTotalSize(),
                ModifiedAt = manifest.ModifiedAt,
                Config = await ModelLayerReader
                    .ReadConfigAsync(_paths, manifest.Manifest, cancellationToken).ConfigureAwait(false)
            });
        }

        // OrderByDescending is a stable sort, so models written in the same second keep the order the
        // directory walk produced them in, as Ollama's SortStableFunc does.
        return summaries.OrderByDescending(summary => summary.ModifiedAt).ToList();
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        return ManifestFiles.ExistsAsync(_paths, parsed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ModelInfo> ShowAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);
        ModelLayerReader layers = await ModelLayerReader
            .ReadAsync(_paths, stored.Manifest, cancellationToken).ConfigureAwait(false);

        GgufMetadata metadata = null;
        if (layers.ModelPath != null)
        {
            metadata = await GgufMetadata.ReadAsync(layers.ModelPath, null, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<GgufMetadata> projectorMetadata = NoMetadata;
        if (layers.ProjectorPaths.Count > 0)
        {
            var projectors = new List<GgufMetadata>(layers.ProjectorPaths.Count);
            foreach (string projectorPath in layers.ProjectorPaths)
            {
                projectors.Add(await GgufMetadata
                    .ReadAsync(projectorPath, null, cancellationToken).ConfigureAwait(false));
            }
            projectorMetadata = projectors;
        }

        return new ModelInfo
        {
            Name = stored.Name,
            DisplayName = stored.Name.DisplayShortest(),
            Digest = stored.Digest,
            Size = stored.Manifest.GetTotalSize(),
            ModifiedAt = stored.ModifiedAt,
            Manifest = stored.Manifest,
            Config = layers.Config,
            Template = layers.Template,
            System = layers.System,
            Parameters = layers.Parameters,
            Licenses = await ReadLicenseTextsAsync(layers, cancellationToken).ConfigureAwait(false),
            License = ReadLicenseRecord(layers.Config),
            Format = ReadFormat(layers),
            DerivedFrom = ReadConfigProperty(layers.Config, ModelConfigKeys.DerivedFrom),
            Tool = ReadConfigProperty(layers.Config, ModelConfigKeys.Tool),
            ToolVersion = ReadConfigProperty(layers.Config, ModelConfigKeys.ToolVersion),
            Settings = ReadConfigSettings(layers.Config),
            Messages = layers.Messages ?? NoMessages,
            Metadata = metadata,
            ProjectorMetadata = projectorMetadata,
            Capabilities = ModelCapabilities.Infer(layers.Config, metadata, projectorMetadata, layers.Template),
            ModelfileText = BuildModelfileText(layers)
        };
    }

    /// <inheritdoc />
    public async Task<ResolvedModel> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);
        ModelLayerReader layers = await ModelLayerReader
            .ReadAsync(_paths, stored.Manifest, cancellationToken).ConfigureAwait(false);

        return new ResolvedModel
        {
            Name = stored.Name,
            ManifestPath = stored.Path,
            ModelPath = layers.ModelPath,
            ModelShardPaths = layers.ModelShardPaths,
            ProjectorPaths = layers.ProjectorPaths,
            AdapterPaths = layers.AdapterPaths,
            DraftPath = layers.DraftPath,
            Template = layers.Template,
            System = layers.System,
            Parameters = layers.Parameters,
            Licenses = layers.LicensesOrEmpty(),
            Messages = layers.Messages ?? NoMessages,
            Config = layers.Config,
            Format = ReadFormat(layers),
            Files = layers.BundleFiles
        };
    }

    /// <inheritdoc />
    public async Task ImportBundleAsync(
        string name,
        string directory,
        ImportOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A directory is required.", nameof(directory));
        }

        ImportOptions effective = options ?? new ImportOptions();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Directory.Exists(root))
        {
            throw new ModelManagerException("the directory " + root + " does not exist");
        }

        IReadOnlyList<string> relativePaths = CollectImportPaths(root, effective.Filter, cancellationToken);
        if (relativePaths.Count == 0)
        {
            throw new ModelManagerException(
                "the directory " + root + " holds no file to import"
                    + (effective.Filter.KeepsEverything ? string.Empty : " that the filter keeps"));
        }

        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        StoredManifest existing = await TryReadManifestAsync(parsed, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> deleteMap = CollectDeleteMap(existing);

        var layers = new List<ModelLayer>(relativePaths.Count);
        foreach (string relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string filePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            ModelLayer layer = await LayerFactory.CreateFromFileAsync(
                _paths, filePath, MediaTypes.BundleFile, null, effective.Link, cancellationToken)
                .ConfigureAwait(false);
            layer.Name = relativePath;
            layers.Add(layer);
            deleteMap.Remove(layer.Digest);
        }

        LicenseRecord license = effective.License ?? FindImportedLicense(relativePaths);
        ModelConfig config = BuildBundleConfig(
            parsed, relativePaths, BundleFormatDetector.Imported, LocalSource, root, null, license);

        await WriteBundleManifestAsync(parsed, layers, config, deleteMap, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ExportResult> ExportToOnnxAsync(
        string name,
        ExportOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        ExportOptions effective = options ?? new ExportOptions();

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);
        ModelLayerReader layers = await ModelLayerReader
            .ReadAsync(_paths, stored.Manifest, cancellationToken).ConfigureAwait(false);

        if (layers.BundleFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "The model " + parsed.DisplayShortest() + " has no publisher file tree, so there is"
                    + " nothing to export: it carries no bundle file layers. Only a bundle - a model"
                    + " pulled with PullOptions or imported from a folder - can be exported.");
        }

        string sourceName = stored.Name.DisplayShortest();
        LicenseRecord license = ReadLicenseRecord(layers.Config);
        string outputName = string.IsNullOrWhiteSpace(effective.OutputName)
            ? DeriveName(stored.Name, OnnxExport.OnnxTag)
            : effective.OutputName.Trim();

        //The checkpoint's own configuration decides two things: which route an automatic export takes,
        //and, for the Optimum route, which task it exports for - Optimum's own inference reads the Hub
        //and refuses a local folder, which is all this library ever hands it.
        string configJson = await ReadBundleFileTextAsync(
            layers.BundleFiles, OnnxExport.ConfigFileName, cancellationToken).ConfigureAwait(false);

        ExportRoute route = effective.Route;
        if (route == ExportRoute.Auto)
        {
            route = OnnxExport.ChooseRoute(layers.BundleFiles, configJson);
        }

        if (!Enum.IsDefined(route))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown ONNX export route.");
        }
        if (OnnxExport.IsMuseCoco(route) && !string.Equals(effective.Precision, "fp32", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("MuseCoco exports FP32. Use ReduceOnnxAsync afterward for INT8 or INT4.", nameof(options));
        }

        IReadOnlyDictionary<string, string> settings = OnnxExport.SettingsFor(route, effective);

        if (route == ExportRoute.PublisherOnnx)
        {
            return await PassThroughOnnxAsync(
                layers, sourceName, outputName, license, settings, effective, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        //Checked before anything is laid out on disk, so that a machine without the modules says so in a
        //second rather than after copying a checkpoint into a temporary folder.
        PythonSupport.Require(_options.Python, OnnxExport.Feature, OnnxExport.ModulesFor(route));

        string work = Path.Combine(
            Path.GetTempPath(), "codebrix-ollama-export-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(work, "source");
        string output = Path.Combine(work, "onnx");
        string cache = Path.Combine(work, "cache");

        try
        {
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(cache);

            Report(progress, "materializing source");
            await BundleMaterializer.WriteAsync(
                layers.BundleFiles,
                input,
                new MaterializeOptions { Link = MaterializeLink.Hardlink, Overwrite = true },
                cancellationToken).ConfigureAwait(false);

            Report(progress, "exporting");
            OnnxExportRun run = await OnnxExport.RunAsync(
                _options.Python,
                route,
                sourceName,
                input,
                output,
                cache,
                effective,
                OnnxExport.TaskFor(OnnxExport.ReadArchitectures(configJson)),
                cancellationToken).ConfigureAwait(false);

            Report(progress, "collecting");
            var provenance = new DerivedProvenance(
                sourceName, run.Tool, run.ToolVersion, settings, BundleFormatDetector.Derived, license);

            Report(progress, "writing manifest");
            IReadOnlyList<string> written = await WriteDerivedBundleAsync(
                outputName, output, provenance, effective.Overwrite, cancellationToken).ConfigureAwait(false);

            Report(progress, "success");
            return new ExportResult(outputName, written, run.Tool, run.ToolVersion, route);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    /// <summary>
    /// Registers the exported graphs a publisher already shipped as a derived bundle of their own.
    /// Nothing is converted, nothing is copied and no interpreter is started.
    /// </summary>
    /// <param name="layers">The decoded layers of the source bundle.</param>
    /// <param name="sourceName">The source model, spelled as it is stored.</param>
    /// <param name="outputName">The name the derived bundle is stored under.</param>
    /// <param name="license">What the source states about the licence, carried over unchanged.</param>
    /// <param name="settings">The options the export was asked for.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <param name="progress">Where progress reports go, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>What was written.</returns>
    /// <exception cref="InvalidOperationException">The source ships no <c>.onnx</c> file.</exception>
    private async Task<ExportResult> PassThroughOnnxAsync(
        ModelLayerReader layers,
        string sourceName,
        string outputName,
        LicenseRecord license,
        IReadOnlyDictionary<string, string> settings,
        ExportOptions options,
        IProgress<PullProgress> progress,
        CancellationToken cancellationToken)
    {
        RequireCoreContract();
        Dictionary<string, HashSet<string>> references = await OnnxExternalFiles
            .ReadBundleAsync(layers.BundleFiles, cancellationToken).ConfigureAwait(false);
        var weights = new HashSet<string>(StringComparer.Ordinal);
        foreach (HashSet<string> graphWeights in references.Values)
        {
            weights.UnionWith(graphWeights);
        }
        IReadOnlyList<ResolvedFile> files = OnnxExport.PassThroughFiles(layers.BundleFiles, weights);
        if (!OnnxExport.HasOnnxFiles(files))
        {
            throw new InvalidOperationException(
                "The model " + sourceName + " ships no .onnx file, so there is nothing for the"
                    + " PublisherOnnx route to register. Leave ExportOptions.Route at Auto, or name"
                    + " GenAiBuilder or Optimum to convert the checkpoint instead.");
        }

        Report(progress, "collecting");
        var provenance = new DerivedProvenance(
            sourceName, OnnxExport.PublisherTool, null, settings, BundleFormatDetector.Onnx, license);

        Report(progress, "writing manifest");
        IReadOnlyList<string> written = await WriteDerivedBundleFromStoreAsync(
            outputName, files, provenance, options.Overwrite, cancellationToken).ConfigureAwait(false);

        Report(progress, "success");
        return new ExportResult(outputName, written, OnnxExport.PublisherTool, null, ExportRoute.PublisherOnnx);
    }

    /// <inheritdoc />
    public async Task<ReduceResult> ReduceOnnxAsync(
        string name,
        ReduceOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        RequireCoreContract();
        ModelName parsed = ParseModelName(name, nameof(name));
        ReduceOptions effective = options ?? new ReduceOptions();

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);
        ModelLayerReader layers = await ModelLayerReader
            .ReadAsync(_paths, stored.Manifest, cancellationToken).ConfigureAwait(false);

        if (layers.BundleFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "The model " + parsed.DisplayShortest() + " has no publisher file tree, so there is"
                    + " nothing to reduce: it carries no bundle file layers. Only a bundle that holds"
                    + " .onnx files can be reduced.");
        }

        IReadOnlyList<string> selected = OnnxReduce.SelectFiles(layers.BundleFiles, effective.Files);
        OnnxReduce.CopyNodeExclusions(effective);
        if (effective.Mode == ReduceMode.PreprocessOnly && effective.NodesToExclude != null && effective.NodesToExclude.Count != 0)
        {
            throw new ArgumentException("NodesToExclude applies to quantization, not PreprocessOnly.", nameof(options));
        }
        Dictionary<string, HashSet<string>> references = await OnnxExternalFiles
            .ReadBundleAsync(layers.BundleFiles, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ResolvedFile> companions = OnnxReduce.CompanionFiles(layers.BundleFiles, selected, references);
        var companionNames = new HashSet<string>(companions.Select(file => file.Name), StringComparer.Ordinal);

        //Resolved before anything is laid out: whether the graphs have been prepared decides which engine can take
        //them, and that is read from the stored blobs themselves - a few hundred bytes each, whatever they weigh.
        bool hasInferMarker = OnnxReduce.NeedsInferMarker(effective.Engine, effective.Mode)
            && await HaveInferMarkerAsync(layers.BundleFiles, selected, cancellationToken).ConfigureAwait(false);
        bool isPythonAvailable =
            OnnxReduce.NeedsPythonAvailability(effective.Engine, effective.Mode, hasInferMarker)
            && OnnxReduce.IsPythonAvailable(_options.Python);
        ReduceEngine engine = OnnxReduce.ResolveEngine(
            effective.Engine, effective.Mode, hasInferMarker, isPythonAvailable);

        string sourceName = stored.Name.DisplayShortest();
        LicenseRecord license = ReadLicenseRecord(layers.Config);
        string outputName = string.IsNullOrWhiteSpace(effective.OutputName)
            ? DeriveName(
                stored.Name, OnnxReduce.AppendTag(stored.Name.Tag, OnnxReduce.TagFor(effective.Mode)))
            : effective.OutputName.Trim();

        //Checked before anything is laid out on disk, so that a machine without the modules says so in a
        //second rather than after a whole bundle has been linked into a temporary folder.
        OnnxReduce.RequireEngine(_options.Python, engine);

        string work = Path.Combine(
            Path.GetTempPath(), "codebrix-ollama-reduce-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(work, "source");
        string output = Path.Combine(work, "reduced");
        string stage = Path.Combine(work, "prepared");

        try
        {
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(output);

            Report(progress, "materializing source");
            await BundleMaterializer.WriteAsync(
                layers.BundleFiles,
                input,
                new MaterializeOptions { Link = MaterializeLink.Hardlink, Overwrite = true },
                cancellationToken).ConfigureAwait(false);

            long sourceBytes = 0;
            var measured = new HashSet<string>(selected, StringComparer.Ordinal);
            foreach (string graph in selected)
            {
                measured.UnionWith(references[graph]);
            }
            foreach (ResolvedFile file in layers.BundleFiles)
            {
                if (measured.Contains(file.Name))
                {
                    sourceBytes += file.Size;
                }
            }
            long reducedBytes = 0;
            string tool = engine == ReduceEngine.Managed ? OnnxReduce.ManagedTool : OnnxReduce.Tool;
            string toolVersion = null;

            foreach (string relativePath in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, "reducing " + relativePath);

                string from = Path.Combine(input, relativePath.Replace('/', Path.DirectorySeparatorChar));
                string to = Path.Combine(output, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));

                //The tools read a graph's weights through the ONNX package, which refuses a file of
                //weights that has more than one link to its content; the layout above made links.
                if (engine == ReduceEngine.Python)
                {
                    await OnnxReduce.UnlinkExternalDataAsync(from, cancellationToken, input).ConfigureAwait(false);
                }

                OnnxReduceRun run = await OnnxReduce.RunAsync(
                    _options.Python, engine, effective.Mode, effective, from, to, stage, cancellationToken, input)
                    .ConfigureAwait(false);

                bool renamed = await OnnxExternalFiles.AvoidCollisionsAsync(to, output, companionNames, cancellationToken)
                    .ConfigureAwait(false);
                reducedBytes += renamed
                    ? await OnnxReduce.MeasureGraphAsync(to, cancellationToken, output).ConfigureAwait(false)
                    : run.TotalBytes;
                tool = run.Tool ?? tool;
                toolVersion = run.ToolVersion ?? toolVersion;

                //The prepared copy of one graph is of no use once that graph has been quantized, and
                //keeping it would double what the temporary folder holds for the next one.
                TryDeleteDirectory(stage);
            }

            Report(progress, "collecting");
            CarryThroughFiles(companions, input, output, cancellationToken);

            IReadOnlyDictionary<string, string> settings =
                OnnxReduce.SettingsFor(effective.Mode, engine, effective, selected);
            var provenance = new DerivedProvenance(
                sourceName, tool, toolVersion, settings, BundleFormatDetector.Derived, license);

            Report(progress, "writing manifest");
            IReadOnlyList<string> written = await WriteDerivedBundleAsync(
                outputName, output, provenance, effective.Overwrite, cancellationToken).ConfigureAwait(false);

            Report(progress, "success");
            return new ReduceResult(
                outputName, written, engine, effective.Mode, sourceBytes, reducedBytes, tool, toolVersion);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    /// <inheritdoc />
    public async Task<ConvertResult> ConvertToGgufAsync(
        string name,
        ConvertOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        RequireCoreContract();
        ModelName parsed = ParseModelName(name, nameof(name));
        ConvertOptions effective = options ?? new ConvertOptions();

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);
        ModelLayerReader layers = await ModelLayerReader
            .ReadAsync(_paths, stored.Manifest, cancellationToken).ConfigureAwait(false);

        if (layers.BundleFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "The model " + parsed.DisplayShortest() + " has no publisher file tree, so there is nothing"
                    + " to convert: it carries no bundle file layers. Only a bundle - a model pulled with"
                    + " PullOptions or imported from a folder - holds a checkpoint.");
        }

        string sourceName = stored.Name.DisplayShortest();
        LicenseRecord license = ReadLicenseRecord(layers.Config);
        string outputName = string.IsNullOrWhiteSpace(effective.OutputName)
            ? DeriveName(
                stored.Name, OnnxReduce.AppendTag(stored.Name.Tag, GgufConvert.TagFor(effective.OutputType)))
            : effective.OutputName.Trim();

        //Everything that can refuse the source is read out of the stored blobs first, so that a model that
        //was never going to convert says so in a moment rather than after gigabytes have been laid out.
        GgufConvert.RequireCheckpoint(layers.BundleFiles, sourceName);
        string configJson = await ReadBundleFileTextAsync(
            layers.BundleFiles, GgufConvert.ConfigFileName, cancellationToken).ConfigureAwait(false);
        GgufConvert.RequireSupportedArchitecture(configJson, effective.Architecture, sourceName);

        string tokenizerConfigJson = await ReadBundleFileTextAsync(
            layers.BundleFiles, GgufConvert.TokenizerConfigFileName, cancellationToken).ConfigureAwait(false);
        string licenseText = await ReadBundleLicenseTextAsync(layers.BundleFiles, cancellationToken)
            .ConfigureAwait(false);

        //The model identifier the general metadata is derived from is the last segment of the stored name,
        //which is the publisher's own model name; the engine's converter takes the same string from the
        //directory it is pointed at, and the two agree because the store knows the name and the folder does not.
        string modelId = string.IsNullOrWhiteSpace(effective.ModelId)
            ? stored.Name.Model
            : effective.ModelId.Trim();

        string work = Path.Combine(
            Path.GetTempPath(), "codebrix-ollama-convert-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(work, "source");
        string output = Path.Combine(work, "gguf");

        try
        {
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(output);

            Report(progress, "materializing source");
            await BundleMaterializer.WriteAsync(
                layers.BundleFiles,
                input,
                new MaterializeOptions { Link = MaterializeLink.Hardlink, Overwrite = true },
                cancellationToken).ConfigureAwait(false);

            var conversion = new ConvertOptions
            {
                OutputType = effective.OutputType,
                Architecture = effective.Architecture,
                ModelId = modelId,
                AddedSpecialTokens = effective.AddedSpecialTokens
            };

            string file = Path.Combine(output, GgufConvert.OutputFileName);
            ConvertResult written = await GgufConversion
                .ConvertAsync(input, file, conversion, progress, cancellationToken).ConfigureAwait(false);

            Report(progress, GgufConvert.CreatingStatus);
            var provenance = new DerivedProvenance(
                sourceName,
                written.Tool,
                written.ToolVersion,
                GgufConvert.SettingsFor(effective, modelId, written),
                BundleFormatDetector.Derived,
                license);

            await CreateAsync(
                outputName,
                GgufConvert.BuildModelfile(file, tokenizerConfigJson, licenseText),
                null,
                provenance,
                effective.Overwrite,
                cancellationToken).ConfigureAwait(false);

            Report(progress, "success");
            return new ConvertResult(outputName, written.Architecture, written.TensorCount, written.OutputBytes,
                written.SourceBytes, written.TypeWritten, written.Tool, written.ToolVersion);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    /// <inheritdoc />
    public async Task<QuantizeGgufResult> QuantizeGgufAsync(
        string name,
        QuantizeGgufOptions options,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.Quantizer == null)
        {
            throw new ArgumentException(
                "A quantization needs a quantizer: this library has none of its own. Set Quantizer in the"
                    + " options to something that reads a GGUF file and writes a quantized copy of it -"
                    + " CodeBrix.Ollama.ModelRunner's QuantizeAsync is one.",
                nameof(options));
        }

        string type;
        try
        {
            type = GgufQuantize.NormalizeType(options.Type);
        }
        catch (ArgumentException error)
        {
            //The type arrives on the options, so the refusal names the parameter the caller actually passed.
            throw new ArgumentException(error.Message, nameof(options), error);
        }

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);
        ModelLayerReader layers = await ModelLayerReader
            .ReadAsync(_paths, stored.Manifest, cancellationToken).ConfigureAwait(false);

        string sourceName = stored.Name.DisplayShortest();
        GgufQuantize.RequireGgufModel(layers, sourceName);

        LicenseRecord license = ReadLicenseRecord(layers.Config);
        string outputName = string.IsNullOrWhiteSpace(options.OutputName)
            ? DeriveName(stored.Name, OnnxReduce.AppendTag(stored.Name.Tag, GgufQuantize.TagFor(type)))
            : options.OutputName.Trim();

        //The stored blob is read where it lies: a quantization reads a file and writes another, and copying
        //gigabytes into a working folder first would buy nothing at all.
        string input = layers.ModelPath;
        long sourceBytes = new FileInfo(input).Length;

        string work = Path.Combine(
            Path.GetTempPath(), "codebrix-ollama-quantize-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(work);
            string file = Path.Combine(work, GgufQuantize.OutputFileName);

            Report(progress, GgufQuantize.QuantizingStatus);
            await options.Quantizer(input, file, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(file))
            {
                throw new ModelManagerException(
                    "the quantizer returned without writing " + file + ", so there is nothing to store");
            }

            long outputBytes = new FileInfo(file).Length;

            Report(progress, GgufConvert.CreatingStatus);
            var provenance = new DerivedProvenance(
                sourceName,
                options.Tool,
                options.ToolVersion,
                GgufQuantize.SettingsFor(type),
                BundleFormatDetector.Derived,
                license);

            await CreateAsync(
                outputName,
                new Modelfile(BuildModelfileCommands(layers, file)),
                null,
                provenance,
                options.Overwrite,
                cancellationToken).ConfigureAwait(false);

            Report(progress, "success");
            return new QuantizeGgufResult(
                outputName, sourceName, type, sourceBytes, outputBytes, options.Tool, options.ToolVersion);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    /// <summary>
    /// Reads the licence TEXT a bundle ships, when it ships one, so that a model derived from it carries
    /// the same text rather than only the identifier its config records.
    /// </summary>
    /// <param name="files">The source bundle's files.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The text, or <see langword="null"/> when the bundle ships no licence file.</returns>
    private async Task<string> ReadBundleLicenseTextAsync(
        IReadOnlyList<ResolvedFile> files, CancellationToken cancellationToken)
    {
        foreach (ResolvedFile file in files)
        {
            if (!IsLicenseFileName(file.Name))
            {
                continue;
            }

            try
            {
                string text = await LayerFactory
                    .ReadBlobTextAsync(_paths, file.Digest, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (ModelManagerException)
            {
                //A file the manifest names but the store no longer holds states no licence.
            }
        }

        return null;
    }

    /// <summary>
    /// Whether every graph a reduction is about to run over records having been through shape inference.
    /// </summary>
    /// <param name="files">The source bundle's files.</param>
    /// <param name="selected">The graphs being reduced.</param>
    /// <param name="cancellationToken">A token that cancels the reads.</param>
    /// <returns><see langword="true"/> when they all carry the marker.</returns>
    /// <remarks>
    /// The blobs are read where they lie, because this happens before anything is laid out, and only the outermost
    /// message of each is walked. One graph without the marker answers for the whole bundle: a reduction writes one
    /// bundle with one engine, so the engine has to be one every graph in it can be taken by.
    /// </remarks>
    private static async Task<bool> HaveInferMarkerAsync(
        IReadOnlyList<ResolvedFile> files,
        IReadOnlyList<string> selected,
        CancellationToken cancellationToken)
    {
        foreach (string relativePath in selected)
        {
            string blobPath = null;
            foreach (ResolvedFile file in files)
            {
                if (string.Equals(file.Name, relativePath, StringComparison.Ordinal))
                {
                    blobPath = file.BlobPath;
                    break;
                }
            }

            if (blobPath == null
                || !await OnnxMetadataProbe.HasMetadataEntryAsync(
                        blobPath,
                        OnnxQuantizationUtilities.InferMetadataKey,
                        OnnxQuantizationUtilities.InferMetadataValue,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }
        }

        return selected.Count > 0;
    }

    /// <summary>
    /// Puts the files a reduction does not touch beside the ones it wrote, so that what is stored is the
    /// whole model and not only its graphs. They are hard-linked where the file system allows it, so a
    /// tokenizer or a graph nobody asked to reduce costs nothing to carry.
    /// </summary>
    /// <param name="files">The files to carry through.</param>
    /// <param name="input">The folder the source was laid out in.</param>
    /// <param name="output">The folder the reduced graphs were written into.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    private static void CarryThroughFiles(
        IReadOnlyList<ResolvedFile> files,
        string input,
        string output,
        CancellationToken cancellationToken)
    {
        foreach (ResolvedFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = file.Name.Replace('/', Path.DirectorySeparatorChar);
            string from = Path.Combine(input, relativePath);
            string to = Path.Combine(output, relativePath);

            if (!File.Exists(from) || File.Exists(to))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (!HardLink.TryCreate(from, to))
            {
                File.Copy(from, to, false);
            }
        }
    }

    /// <summary>
    /// The name a derived bundle takes when the caller names none: the source name with its tag
    /// replaced.
    /// </summary>
    /// <param name="source">The source model name.</param>
    /// <param name="tag">The tag the derived bundle takes, for example "onnx".</param>
    /// <returns>The derived name.</returns>
    private static string DeriveName(ModelName source, string tag)
        => new ModelName(source.Host, source.Namespace, source.Model, tag, source.ProtocolScheme)
            .DisplayShortest();

    /// <summary>
    /// Reads one of a bundle's files as text, by the publisher's path.
    /// </summary>
    /// <param name="files">The bundle's files.</param>
    /// <param name="path">The relative path to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The text, or <see langword="null"/> when the bundle has no such file.</returns>
    private async Task<string> ReadBundleFileTextAsync(
        IReadOnlyList<ResolvedFile> files, string path, CancellationToken cancellationToken)
    {
        foreach (ResolvedFile file in files)
        {
            if (!string.Equals(file.Name, path, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                return await LayerFactory
                    .ReadBlobTextAsync(_paths, file.Digest, cancellationToken).ConfigureAwait(false);
            }
            catch (ModelManagerException)
            {
                //A file the manifest names but the store no longer holds decides nothing about the route.
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Hands one status to a caller that asked for progress, and does nothing for one that did not.
    /// </summary>
    /// <param name="progress">Where reports go, or <see langword="null"/>.</param>
    /// <param name="status">The status to report.</param>
    private static void Report(IProgress<PullProgress> progress, string status)
        => progress?.Report(new PullProgress(status));

    /// <summary>
    /// Removes a working directory, and says nothing when it cannot: a temporary folder that outlives
    /// its export is untidy, never a failure of the export.
    /// </summary>
    /// <param name="directory">The directory to remove.</param>
    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
            //As above.
        }
        catch (UnauthorizedAccessException)
        {
            //As above.
        }
    }

    /// <summary>
    /// Writes a bundle THIS LIBRARY produced, from a folder a tool has just filled, and records how it
    /// was produced. It is the writing half of every derived artifact - an export, and later a reduction
    /// - and the only way a config gains the provenance fields.
    /// </summary>
    /// <remarks>
    /// Files are hard-linked into the blobs directory where the file system allows it, because the
    /// folder a tool wrote into is about to be deleted; a link that cannot be made becomes a copy, as
    /// everywhere else in this library.
    /// </remarks>
    /// <param name="name">The name the derived bundle is stored under.</param>
    /// <param name="directory">The folder the tool wrote. It is walked to its full depth.</param>
    /// <param name="provenance">What produced these files, and from what.</param>
    /// <param name="overwrite">Whether a bundle already stored under the name may be replaced.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The publisher-style relative paths that were stored, in manifest order.</returns>
    /// <exception cref="ModelManagerException">
    /// The folder does not exist or is empty, or the name is taken and <paramref name="overwrite"/> is
    /// <see langword="false"/>.
    /// </exception>
    internal async Task<IReadOnlyList<string>> WriteDerivedBundleAsync(
        string name,
        string directory,
        DerivedProvenance provenance,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        if (provenance == null)
        {
            throw new ArgumentNullException(nameof(provenance));
        }
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A directory is required.", nameof(directory));
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Directory.Exists(root))
        {
            throw new ModelManagerException("the directory " + root + " does not exist");
        }

        IReadOnlyList<string> relativePaths = CollectImportPaths(root, FileFilter.Default, cancellationToken);
        if (relativePaths.Count == 0)
        {
            throw new ModelManagerException("the directory " + root + " holds no file to store");
        }

        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> deleteMap = await PrepareDerivedAsync(
            parsed, name, overwrite, cancellationToken).ConfigureAwait(false);

        var layers = new List<ModelLayer>(relativePaths.Count);
        foreach (string relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string filePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            ModelLayer layer = await LayerFactory.CreateFromFileAsync(
                _paths, filePath, MediaTypes.BundleFile, provenance.SourceName, true, cancellationToken)
                .ConfigureAwait(false);
            layer.Name = relativePath;
            layers.Add(layer);
            deleteMap.Remove(layer.Digest);
        }

        ModelConfig config = BuildDerivedConfig(parsed, relativePaths, provenance);
        await WriteBundleManifestAsync(parsed, layers, config, deleteMap, cancellationToken).ConfigureAwait(false);
        return relativePaths;
    }

    /// <summary>
    /// Writes a bundle THIS LIBRARY produced out of files that are already in the store, which is what a
    /// pass-through export is: the publisher shipped the files, and all that is added is a bundle of its
    /// own that names them and says where they came from.
    /// </summary>
    /// <remarks>
    /// Not one byte is copied. The store is content addressed, so a second manifest naming the same
    /// digests shares the same blobs, exactly as <see cref="CopyAsync"/> does; deleting either bundle
    /// leaves the other's files alone, because a blob is removed only once no manifest names it.
    /// </remarks>
    /// <param name="name">The name the derived bundle is stored under.</param>
    /// <param name="files">The files to name, as <see cref="ResolveAsync"/> reported them.</param>
    /// <param name="provenance">What produced these files, and from what.</param>
    /// <param name="overwrite">Whether a bundle already stored under the name may be replaced.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The publisher-style relative paths that were stored, in manifest order.</returns>
    /// <exception cref="ModelManagerException">
    /// There are no files, one of their blobs is not in the store, or the name is taken and
    /// <paramref name="overwrite"/> is <see langword="false"/>.
    /// </exception>
    internal async Task<IReadOnlyList<string>> WriteDerivedBundleFromStoreAsync(
        string name,
        IReadOnlyList<ResolvedFile> files,
        DerivedProvenance provenance,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        if (provenance == null)
        {
            throw new ArgumentNullException(nameof(provenance));
        }
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }
        if (files.Count == 0)
        {
            throw new ModelManagerException("there is no file to store under " + parsed.DisplayShortest());
        }

        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> deleteMap = await PrepareDerivedAsync(
            parsed, name, overwrite, cancellationToken).ConfigureAwait(false);

        var layers = new List<ModelLayer>(files.Count);
        var relativePaths = new List<string>(files.Count);
        foreach (ResolvedFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ModelLayer layer = await LayerFactory.CreateFromExistingBlobAsync(
                _paths, file.Digest, MediaTypes.BundleFile, provenance.SourceName, cancellationToken)
                .ConfigureAwait(false);
            layer.Name = file.Name;
            layers.Add(layer);
            relativePaths.Add(file.Name);
            deleteMap.Remove(layer.Digest);
        }

        ModelConfig config = BuildDerivedConfig(parsed, relativePaths, provenance);
        await WriteBundleManifestAsync(parsed, layers, config, deleteMap, cancellationToken).ConfigureAwait(false);
        return relativePaths;
    }

    /// <summary>
    /// Refuses a derived bundle that would replace something the caller did not ask to replace, and
    /// collects what the manifest being replaced referenced.
    /// </summary>
    /// <param name="parsed">The name the derived bundle is stored under.</param>
    /// <param name="name">The name as the caller wrote it, for the message.</param>
    /// <param name="overwrite">Whether an existing bundle may be replaced.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The digests of the manifest being replaced, if there is one.</returns>
    /// <exception cref="ModelManagerException">The name is taken and <paramref name="overwrite"/> is false.</exception>
    private async Task<Dictionary<string, string>> PrepareDerivedAsync(
        ModelName parsed, string name, bool overwrite, CancellationToken cancellationToken)
    {
        StoredManifest existing = await TryReadManifestAsync(parsed, cancellationToken).ConfigureAwait(false);
        if (existing != null && !overwrite)
        {
            throw NameIsTaken(parsed);
        }

        return CollectDeleteMap(existing);
    }

    /// <summary>
    /// The refusal a derived artifact gives when its name is taken and nobody asked for a replacement.
    /// </summary>
    /// <param name="parsed">The name the artifact would have been stored under.</param>
    /// <returns>The exception to throw.</returns>
    private static ModelManagerException NameIsTaken(ModelName parsed)
        => new ModelManagerException(
            "the model " + parsed.DisplayShortest() + " is already in the store; set Overwrite in the"
                + " options to replace it, or choose another name with OutputName");

    /// <summary>
    /// Builds the config layer of a derived bundle: its files, where they came from and what made them.
    /// </summary>
    /// <param name="name">The name the bundle is stored under.</param>
    /// <param name="paths">The relative paths, which decide the model format.</param>
    /// <param name="provenance">What produced the files, and from what.</param>
    /// <returns>The config.</returns>
    private static ModelConfig BuildDerivedConfig(
        ModelName name, IReadOnlyList<string> paths, DerivedProvenance provenance)
        => new ModelConfig
        {
            ModelFormat = BundleFormatDetector.Detect(paths, provenance.Format),
            ModelFamily = name.Model,
            AdditionalProperties = BuildDerivedProperties(provenance)
        };

    /// <summary>
    /// The config properties that say a model was derived: where its source came from, what is known about
    /// the licence, and what produced it. They are the same for a derived BUNDLE and for a model this
    /// library created from a file it wrote itself, so that one reader reads both.
    /// </summary>
    /// <param name="provenance">What produced the files, and from what.</param>
    /// <returns>The properties, in the order they are written.</returns>
    private static Dictionary<string, JsonElement> BuildDerivedProperties(DerivedProvenance provenance)
    {
        LicenseRecord stated = provenance.License;
        string timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [ModelConfigKeys.Source] = ToJsonElement(DerivedSource),
            [ModelConfigKeys.Repository] = ToJsonElement(provenance.SourceName),
            [ModelConfigKeys.Revision] = ToJsonElement(null),
            [ModelConfigKeys.LicenseId] = ToJsonElement(stated.LicenseId),
            [ModelConfigKeys.LicenseSource] = ToJsonElement(stated.LicenseSource),
            [ModelConfigKeys.PulledAt] = ToJsonElement(timestamp),
            [ModelConfigKeys.DerivedFrom] = ToJsonElement(provenance.SourceName),
            [ModelConfigKeys.Tool] = ToJsonElement(provenance.Tool),
            [ModelConfigKeys.ToolVersion] = ToJsonElement(provenance.ToolVersion),
            [ModelConfigKeys.DerivedAt] = ToJsonElement(timestamp),
            [ModelConfigKeys.Settings] = ToSettingsElement(provenance.Settings)
        };
    }

    /// <summary>
    /// Turns the settings a tool was given into the JSON object the config carries.
    /// </summary>
    /// <param name="settings">The settings, which may be empty.</param>
    /// <returns>A JSON object whose values are strings.</returns>
    private static JsonElement ToSettingsElement(IReadOnlyDictionary<string, string> settings)
    {
        var ordered = new Dictionary<string, string>(StringComparer.Ordinal);
        if (settings != null)
        {
            foreach (KeyValuePair<string, string> setting in settings)
            {
                ordered[setting.Key] = setting.Value;
            }
        }

        return JsonSerializer.SerializeToElement(ordered, ModelManagerJson.Options);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> MaterializeAsync(
        string name,
        string targetDirectory,
        MaterializeOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("A target directory is required.", nameof(targetDirectory));
        }

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);
        ModelLayerReader layers = await ModelLayerReader
            .ReadAsync(_paths, stored.Manifest, cancellationToken).ConfigureAwait(false);

        if (layers.BundleFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "The model " + parsed.DisplayShortest() + " has no publisher file tree, so there is nothing"
                    + " to lay out: it carries no bundle file layers. Only a bundle - a model pulled with"
                    + " PullOptions or imported from a folder - can be materialized; the files of a GGUF"
                    + " model are named by ResolveAsync instead.");
        }

        return await BundleMaterializer.WriteAsync(
            layers.BundleFiles, targetDirectory, options ?? new MaterializeOptions(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CopyAsync(
        string sourceName,
        string destinationName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName source = ParseModelName(sourceName, nameof(sourceName));
        ModelName destination = ParseModelName(destinationName, nameof(destinationName));

        if (string.Equals(source.ToRelativePath(), destination.ToRelativePath(), StringComparison.Ordinal))
        {
            return;
        }

        StoredManifest stored = await RequireManifestAsync(source, sourceName, cancellationToken).ConfigureAwait(false);

        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        await ManifestFiles
            .WriteAsync(_paths, destination, stored.RawBytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ModelName parsed = ParseModelName(name, nameof(name));

        StoredManifest stored = await RequireManifestAsync(parsed, name, cancellationToken).ConfigureAwait(false);

        await ManifestFiles.DeleteAsync(_paths, parsed, cancellationToken).ConfigureAwait(false);
        await LayerPruner
            .RemoveUnreferencedAsync(_paths, CollectDigests(stored.Manifest), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PruneAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (gracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gracePeriod), "The grace period cannot be negative.");
        }

        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        return await LayerPruner.PruneAllAsync(_paths, gracePeriod, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task CreateAsync(
        string name,
        Modelfile modelfile,
        CreateOptions options = null,
        CancellationToken cancellationToken = default)
        => CreateAsync(name, modelfile, options, null, true, cancellationToken);

    /// <summary>
    /// Creates a model from a Modelfile and records that THIS LIBRARY produced it, which is what a
    /// conversion is: a file this library wrote, stored through the same create path a person's own
    /// Modelfile takes, with the provenance keys a derived artifact carries written into its config.
    /// </summary>
    /// <remarks>
    /// It is internal for the reason <see cref="DerivedProvenance"/> is: provenance is a statement about
    /// work this library did, so the only way to write one is to have this library do that work.
    /// </remarks>
    /// <param name="name">The name to give the new model.</param>
    /// <param name="modelfile">The synthesized Modelfile.</param>
    /// <param name="options">Options, or <see langword="null"/> for the defaults.</param>
    /// <param name="provenance">What produced the file, and from what, or <see langword="null"/> for none.</param>
    /// <param name="overwrite">
    /// Whether a model already stored under the name may be replaced. It is honoured only when a
    /// provenance is given; the public create has always replaced what was there.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the manifest has been written.</returns>
    /// <exception cref="ModelManagerException">The name is taken and <paramref name="overwrite"/> is false.</exception>
    internal async Task CreateAsync(
        string name,
        Modelfile modelfile,
        CreateOptions options,
        DerivedProvenance provenance,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (modelfile == null)
        {
            throw new ArgumentNullException(nameof(modelfile));
        }

        ModelName parsed = ParseModelName(name, nameof(name));

        if (modelfile.Drafts.Count > 0)
        {
            throw new ModelManagerException("DRAFT is not supported by CodeBrix.Ollama.ModelManager");
        }
        if (modelfile.ModelArgs.Count == 0)
        {
            throw new ModelManagerException("no FROM line");
        }

        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);

        string baseDirectory = options != null && !string.IsNullOrEmpty(options.BaseDirectory)
            ? options.BaseDirectory
            : Environment.CurrentDirectory;

        StoredManifest oldManifest = await TryReadManifestAsync(parsed, cancellationToken).ConfigureAwait(false);
        if (provenance != null && oldManifest != null && !overwrite)
        {
            throw NameIsTaken(parsed);
        }

        var layers = new List<ModelLayer>();
        var config = new ModelConfig();

        // What a layer records as its source: the FROM argument for a model a person created, and the model
        // it was derived FROM for one this library made, because the file that one names is a temporary
        // file that was deleted before anybody could read the manifest.
        string sourceLabel = provenance == null ? null : provenance.SourceName;

        // The first FROM argument decides where everything else comes from: a file on disk is imported
        // as a new blob, a name already in the store has its layers and config inherited.
        string firstArgument = modelfile.ModelArgs[0];
        string firstPath = ResolveArgumentPath(baseDirectory, firstArgument);
        if (File.Exists(firstPath))
        {
            layers.Add(await ImportGgufFileAsync(
                    firstPath, sourceLabel ?? firstArgument, config, cancellationToken)
                .ConfigureAwait(false));
        }
        else
        {
            config = await InheritFromModelAsync(firstArgument, layers, cancellationToken).ConfigureAwait(false);
        }

        // Further FROM arguments are projectors in practice, and are imported the same way.
        for (int i = 1; i < modelfile.ModelArgs.Count; i++)
        {
            string argument = modelfile.ModelArgs[i];
            string path = ResolveArgumentPath(baseDirectory, argument);
            if (!File.Exists(path))
            {
                throw new ModelNotFoundException(argument, $"'{argument}' is neither a file nor a model in the store");
            }
            layers.Add(await ImportGgufFileAsync(
                    path, sourceLabel ?? argument, config, cancellationToken)
                .ConfigureAwait(false));
        }

        foreach (string adapterArgument in modelfile.Adapters)
        {
            string path = ResolveArgumentPath(baseDirectory, adapterArgument);
            if (!File.Exists(path))
            {
                throw new ModelManagerException($"file {path} does not exist");
            }

            GgufMetadata metadata = await GgufMetadata
                .ReadAsync(path, SingleElementReadOptions(), cancellationToken).ConfigureAwait(false);
            if (!string.Equals(metadata.Kind, "adapter", StringComparison.Ordinal))
            {
                throw new GgufFormatException($"{path} is not a LoRA adapter");
            }

            layers.Add(await LayerFactory
                .CreateFromFileAsync(_paths, path, MediaTypes.Adapter, adapterArgument, cancellationToken)
                .ConfigureAwait(false));
        }

        await ApplyModelfileLayersAsync(layers, modelfile, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(modelfile.Renderer))
        {
            config.Renderer = modelfile.Renderer;
        }
        if (!string.IsNullOrEmpty(modelfile.Parser))
        {
            config.Parser = modelfile.Parser;
        }
        if (!string.IsNullOrEmpty(modelfile.Requires))
        {
            config.Requires = modelfile.Requires;
        }

        if (provenance != null)
        {
            config.AdditionalProperties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonElement> property in BuildDerivedProperties(provenance))
            {
                config.AdditionalProperties[property.Key] = property.Value;
            }
        }

        ModelLayer configLayer = await LayerFactory.CreateFromBytesAsync(
            _paths,
            ModelManagerJson.SerializeLikeGo(config),
            MediaTypes.Config,
            cancellationToken).ConfigureAwait(false);

        var manifest = new ModelManifest
        {
            Config = configLayer,
            Layers = layers
        };
        await ManifestFiles.WriteAsync(_paths, parsed, manifest, cancellationToken).ConfigureAwait(false);

        if (oldManifest != null)
        {
            await LayerPruner
                .RemoveUnreferencedAsync(_paths, CollectDigests(oldManifest.Manifest), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the registry client and the connections it holds. The store directory is untouched.
    /// </summary>
    public void Dispose()
    {
        RegistryClient client;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            client = _registryClient;
            _registryClient = null;
        }

        client?.Dispose();
    }

    /// <summary>
    /// Runs one pull to completion, writing every progress report into the channel the iterator reads.
    /// This is the port of Ollama's <c>PullModel</c>.
    /// </summary>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="writer">Where progress reports go.</param>
    /// <param name="cancellationToken">A token that cancels the pull.</param>
    /// <returns>A task that completes when the manifest has been written.</returns>
    private async Task RunPullAsync(
        ModelName name,
        ChannelWriter<PullProgress> writer,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);

        // Whatever the previous manifest referenced and the new one does not is removed at the end.
        StoredManifest existing = await TryReadManifestAsync(name, cancellationToken).ConfigureAwait(false);
        var deleteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (existing != null)
        {
            foreach (string digest in CollectDigests(existing.Manifest))
            {
                deleteMap[digest] = digest;
            }
        }

        writer.TryWrite(new PullProgress("pulling manifest"));

        RegistryClient client = GetRegistryClient();
        RegistryManifestResponse response = await client
            .GetManifestAsync(name, cancellationToken).ConfigureAwait(false);
        ModelManifest manifest = response.Manifest;

        if (manifest.Layers != null)
        {
            foreach (ModelLayer layer in manifest.Layers)
            {
                if (layer != null && string.Equals(layer.MediaType, MediaTypes.Tensor, StringComparison.Ordinal))
                {
                    throw new ModelManagerException(
                        $"model {name.DisplayShortest()} is a safetensors model, which this library cannot pull or run");
                }
            }
        }

        var layersToPull = new List<ModelLayer>();
        if (manifest.Layers != null)
        {
            layersToPull.AddRange(manifest.Layers);
        }
        if (manifest.Config != null && !string.IsNullOrEmpty(manifest.Config.Digest))
        {
            layersToPull.Add(manifest.Config);
        }

        var skipVerify = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (ModelLayer layer in layersToPull)
        {
            bool cacheHit = await DownloadLayerAsync(name, layer, writer, cancellationToken).ConfigureAwait(false);

            // A digest that the config and a content layer share must still be verified when either of
            // the two was downloaded: the second pass would otherwise see the file the first pass wrote
            // and call it a cache hit.
            skipVerify[layer.Digest] = skipVerify.TryGetValue(layer.Digest, out bool alreadySkipped)
                ? alreadySkipped && cacheHit
                : cacheHit;
            deleteMap.Remove(layer.Digest);
        }

        writer.TryWrite(new PullProgress("verifying sha256 digest"));
        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModelLayer layer in layersToPull)
        {
            if (skipVerify[layer.Digest] || !verified.Add(layer.Digest))
            {
                continue;
            }
            await LayerFactory.VerifyBlobAsync(_paths, layer.Digest, cancellationToken).ConfigureAwait(false);
        }

        writer.TryWrite(new PullProgress("writing manifest"));
        await ManifestFiles.WriteAsync(_paths, name, response.RawBytes, cancellationToken).ConfigureAwait(false);

        if (deleteMap.Count > 0)
        {
            writer.TryWrite(new PullProgress("removing unused layers"));
            await LayerPruner
                .RemoveUnreferencedAsync(_paths, deleteMap.Values, cancellationToken).ConfigureAwait(false);
        }

        writer.TryWrite(new PullProgress("success"));
    }

    /// <summary>
    /// Makes sure one layer's blob is in the store, downloading it when it is not.
    /// </summary>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="layer">The layer to fetch.</param>
    /// <param name="writer">Where progress reports go.</param>
    /// <param name="cancellationToken">A token that cancels the download.</param>
    /// <returns><see langword="true"/> when the blob was already in the store.</returns>
    private async Task<bool> DownloadLayerAsync(
        ModelName name,
        ModelLayer layer,
        ChannelWriter<PullProgress> writer,
        CancellationToken cancellationToken)
    {
        string digest = layer.Digest;
        string status = "pulling " + Sha256Digest.Short(digest);
        string blobPath = _paths.GetBlobPath(digest);

        var blob = new FileInfo(blobPath);
        if (blob.Exists)
        {
            writer.TryWrite(new PullProgress(status, digest, layer.Size, layer.Size));
            return true;
        }

        RegistryClient client = GetRegistryClient();
        await client.DownloadBlobAsync(
            name,
            digest,
            layer.Size,
            blobPath,
            _paths.GetPartialDataPath(digest),
            _paths.GetPartialStatePath(digest),
            (completed, total) => writer.TryWrite(new PullProgress(status, digest, total, completed)),
            cancellationToken).ConfigureAwait(false);

        return false;
    }

    /// <summary>
    /// Runs one bundle pull to completion: list the source, fetch every file it named into the blobs
    /// directory, then write one layer per file and a config that records where they came from.
    /// </summary>
    /// <remarks>
    /// The verification step reports its status and checks that every blob the new manifest names is in
    /// the store at the size that was recorded, rather than reading every file a second time. Each
    /// file's SHA-256 was computed from the bytes as they were written and checked against whatever the
    /// source stated, so there is nothing a re-read could establish that the download did not - and a
    /// bundle is exactly the case where re-reading would mean gigabytes.
    /// </remarks>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="options">Where the files come from and which of them are wanted.</param>
    /// <param name="writer">Where progress reports go.</param>
    /// <param name="cancellationToken">A token that cancels the pull.</param>
    /// <returns>A task that completes when the manifest has been written.</returns>
    private async Task RunBundlePullAsync(
        ModelName name,
        PullOptions options,
        ChannelWriter<PullProgress> writer,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);

        bool fromHub = options.Source == PullSource.HuggingFaceFiles;
        string repository = fromHub ? ResolveRepository(name, options) : null;
        string revision = fromHub ? ResolveRevision(name, options) : null;

        // Whatever the previous manifest referenced and the new one does not is removed at the end.
        StoredManifest existing = await TryReadManifestAsync(name, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> deleteMap = CollectDeleteMap(existing);

        writer.TryWrite(new PullProgress("listing " + (repository ?? FileListLabel)));
        BundleListing listing = await ListBundleAsync(options, repository, revision, cancellationToken)
            .ConfigureAwait(false);

        if (listing.Files.Count == 0)
        {
            throw new ModelManagerException(
                "the source listed no file to pull for " + name.DisplayShortest()
                    + ", so there would be nothing to store");
        }

        var layers = new List<ModelLayer>(listing.Files.Count);
        var paths = new List<string>(listing.Files.Count);

        using (var downloader = new FileDownloader(GetRegistryClient(), _options, _paths))
        {
            foreach (BundleFile file in listing.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string status = "pulling " + file.Path;

                // A file whose source stated a sha256 is named by it before a byte arrives; a file whose
                // source stated none is named by what it turns out to be, so its digest joins the
                // reports only once the bytes are all there.
                string statedDigest = file.Sha256 == null ? null : Sha256Digest.Prefix + file.Sha256;

                BlobDownloadResult result = await downloader.DownloadFileAsync(
                    file,
                    options.RequireHashes,
                    (completed, total) => writer.TryWrite(new PullProgress(status, statedDigest, total, completed)),
                    cancellationToken).ConfigureAwait(false);

                writer.TryWrite(new PullProgress(status, result.Digest, result.Size, result.Size));

                layers.Add(new ModelLayer(MediaTypes.BundleFile, result.Digest, result.Size)
                {
                    Name = file.Path
                });
                paths.Add(file.Path);
                deleteMap.Remove(result.Digest);
            }
        }

        writer.TryWrite(new PullProgress("verifying sha256 digest"));
        EnsureBundleBlobs(layers);

        ModelConfig config = BuildBundleConfig(
            name,
            paths,
            fromHub ? BundleFormatDetector.HuggingFace : BundleFormatDetector.Files,
            fromHub ? HubSource : UrlSource,
            fromHub ? repository : null,
            listing.ResolvedRevision,
            listing.License);

        writer.TryWrite(new PullProgress("writing manifest"));
        await WriteBundleManifestAsync(name, layers, config, deleteMap, cancellationToken).ConfigureAwait(false);
        writer.TryWrite(new PullProgress("success"));
    }

    /// <summary>
    /// Asks the source of a bundle pull what it holds.
    /// </summary>
    /// <param name="options">The pull options.</param>
    /// <param name="repository">The Hugging Face repository, or <see langword="null"/>.</param>
    /// <param name="revision">The Hugging Face revision, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the requests.</param>
    /// <returns>The files the pull fetches, already filtered.</returns>
    /// <exception cref="ArgumentException">
    /// A file-list pull was asked for with no files, or the source is not one a bundle pull can use.
    /// </exception>
    private async Task<BundleListing> ListBundleAsync(
        PullOptions options,
        string repository,
        string revision,
        CancellationToken cancellationToken)
    {
        if (options.Source == PullSource.HuggingFaceFiles)
        {
            using var hub = new HuggingFaceHubSource(repository, revision, options.Filter, _options);
            return await hub.ListAsync(cancellationToken).ConfigureAwait(false);
        }

        if (options.Source == PullSource.FileList)
        {
            if (options.Files.Count == 0)
            {
                throw new ArgumentException(
                    "A pull from a list of addresses needs at least one file in PullOptions.Files.",
                    nameof(options));
            }

            using var list = new HttpFileListSource(options.Files, options.Filter, _options);
            return await list.ListAsync(cancellationToken).ConfigureAwait(false);
        }

        throw new ArgumentException(
            "PullSource." + options.Source + " is not a source a bundle can be pulled from.", nameof(options));
    }

    /// <summary>
    /// Works out which Hugging Face repository a pull is for: the one the options name, or the one the
    /// model name spells when its host is the Hub.
    /// </summary>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="options">The pull options.</param>
    /// <returns>The repository as <c>&lt;namespace&gt;/&lt;repository&gt;</c>.</returns>
    /// <exception cref="ArgumentException">
    /// The options name no repository and the name's host is not the Hub.
    /// </exception>
    private static string ResolveRepository(ModelName name, PullOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Repository))
        {
            return options.Repository.Trim();
        }

        if (HuggingFaceHubSource.IsHubHost(name.Host))
        {
            return name.Namespace + "/" + name.Model;
        }

        throw new ArgumentException(
            "The name " + name.DisplayShortest() + " does not spell a Hugging Face repository: its host is '"
                + name.Host + "', not " + HuggingFaceHubSource.ShortHubHost + " or "
                + HuggingFaceHubSource.HubHost + ". Set PullOptions.Repository to <namespace>/<repository>,"
                + " or store the bundle under a name whose host is the Hub.",
            nameof(options));
    }

    /// <summary>
    /// Works out which revision a Hugging Face pull lists: the one the options name, or the model name's
    /// own tag, where the store's default tag means the default branch.
    /// </summary>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="options">The pull options.</param>
    /// <returns>The branch, tag or commit to list.</returns>
    private static string ResolveRevision(ModelName name, PullOptions options)
    {
        return HuggingFaceHubSource.NormalizeRevision(
            string.IsNullOrWhiteSpace(options.Revision) ? name.Tag : options.Revision);
    }

    /// <summary>
    /// Checks that every blob a new bundle manifest is about to name is in the store at the size the
    /// layer records.
    /// </summary>
    /// <param name="layers">The layers of the manifest about to be written.</param>
    /// <exception cref="ModelManagerException">A blob is missing or is not the size it should be.</exception>
    private void EnsureBundleBlobs(IReadOnlyList<ModelLayer> layers)
    {
        foreach (ModelLayer layer in layers)
        {
            var blob = new FileInfo(_paths.GetBlobPath(layer.Digest));
            if (!blob.Exists)
            {
                throw new ModelManagerException(
                    "blob " + layer.Digest + " of '" + layer.Name + "' does not exist");
            }
            if (blob.Length != layer.Size)
            {
                throw new ModelManagerException(
                    "blob " + layer.Digest + " of '" + layer.Name + "' is "
                        + blob.Length.ToString(CultureInfo.InvariantCulture) + " bytes, not "
                        + layer.Size.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>
    /// Builds the config layer of a bundle: what its files are, and where they came from.
    /// </summary>
    /// <param name="name">The name the bundle is stored under.</param>
    /// <param name="paths">The publisher's relative paths, which decide the model format.</param>
    /// <param name="undecidedFormat">The format to record when the file names decide nothing.</param>
    /// <param name="source">Where the files came from: the Hub, an address, or a folder on disk.</param>
    /// <param name="repository">The repository or folder, or <see langword="null"/>.</param>
    /// <param name="revision">The commit the listing resolved to, or <see langword="null"/>.</param>
    /// <param name="license">What the source states about the licence, or <see langword="null"/>.</param>
    /// <returns>The config.</returns>
    private static ModelConfig BuildBundleConfig(
        ModelName name,
        IReadOnlyList<string> paths,
        string undecidedFormat,
        string source,
        string repository,
        string revision,
        LicenseRecord license)
    {
        LicenseRecord stated = license ?? LicenseRecord.None;

        return new ModelConfig
        {
            ModelFormat = BundleFormatDetector.Detect(paths, undecidedFormat),
            ModelFamily = name.Model,
            AdditionalProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [ModelConfigKeys.Source] = ToJsonElement(source),
                [ModelConfigKeys.Repository] = ToJsonElement(repository),
                [ModelConfigKeys.Revision] = ToJsonElement(revision),
                [ModelConfigKeys.LicenseId] = ToJsonElement(stated.LicenseId),
                [ModelConfigKeys.LicenseSource] = ToJsonElement(stated.LicenseSource),
                [ModelConfigKeys.PulledAt] = ToJsonElement(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
            }
        };
    }

    /// <summary>
    /// Writes the config blob and the manifest of a bundle, then removes the blobs the manifest that was
    /// there before named and this one does not.
    /// </summary>
    /// <param name="name">The name the bundle is stored under.</param>
    /// <param name="layers">The file layers, in the order they are recorded.</param>
    /// <param name="config">The config to write.</param>
    /// <param name="deleteMap">The digests of the previous manifest that no longer have a use.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>A task that completes when the manifest is in place.</returns>
    private async Task WriteBundleManifestAsync(
        ModelName name,
        List<ModelLayer> layers,
        ModelConfig config,
        Dictionary<string, string> deleteMap,
        CancellationToken cancellationToken)
    {
        ModelLayer configLayer = await LayerFactory.CreateFromBytesAsync(
            _paths,
            ModelManagerJson.SerializeLikeGo(config),
            MediaTypes.Config,
            cancellationToken).ConfigureAwait(false);
        deleteMap.Remove(configLayer.Digest);

        var manifest = new ModelManifest
        {
            Config = configLayer,
            Layers = layers
        };
        await ManifestFiles.WriteAsync(_paths, name, manifest, cancellationToken).ConfigureAwait(false);

        if (deleteMap.Count > 0)
        {
            await LayerPruner
                .RemoveUnreferencedAsync(_paths, deleteMap.Values, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The digests a manifest that is about to be replaced names, keyed so that the ones the new
    /// manifest names again can be struck off as they are stored.
    /// </summary>
    /// <param name="existing">The manifest that is there now, or <see langword="null"/>.</param>
    /// <returns>The digests, which are deleted at the end of the pull unless they are struck off.</returns>
    private static Dictionary<string, string> CollectDeleteMap(StoredManifest existing)
    {
        var deleteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (existing == null)
        {
            return deleteMap;
        }

        foreach (string digest in CollectDigests(existing.Manifest))
        {
            deleteMap[digest] = digest;
        }
        return deleteMap;
    }

    /// <summary>
    /// Walks a folder to its full depth and returns the paths, relative to it and with forward slashes,
    /// of the files an import keeps, in a fixed order so that two imports of one folder write the same
    /// manifest.
    /// </summary>
    /// <param name="root">The absolute folder path.</param>
    /// <param name="filter">Which files are wanted.</param>
    /// <param name="cancellationToken">A token that cancels the walk.</param>
    /// <returns>The relative paths, ordered.</returns>
    private static IReadOnlyList<string> CollectImportPaths(
        string root, FileFilter filter, CancellationToken cancellationToken)
    {
        var kept = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (filter.ShouldInclude(relativePath))
            {
                kept.Add(relativePath);
            }
        }

        kept.Sort(StringComparer.Ordinal);
        return kept;
    }

    /// <summary>
    /// The licence an imported folder states by holding a licence file, when the caller stated none.
    /// </summary>
    /// <param name="relativePaths">The paths being imported.</param>
    /// <returns>
    /// A record naming the file the terms are in, or <see cref="LicenseRecord.None"/> when there is no
    /// such file. No identifier is invented: what a licence file says is for a human to read.
    /// </returns>
    private static LicenseRecord FindImportedLicense(IReadOnlyList<string> relativePaths)
    {
        foreach (string path in relativePaths)
        {
            if (IsLicenseFileName(path))
            {
                return new LicenseRecord(null, path, "The folder states no licence; it holds this file.");
            }
        }
        return LicenseRecord.None;
    }

    /// <summary>
    /// Whether a relative path names a licence file, which is <c>LICENSE</c> or <c>LICENSE.</c> and
    /// anything, ignoring case.
    /// </summary>
    /// <param name="path">The relative path, with forward slashes.</param>
    /// <returns><see langword="true"/> when the file is a licence.</returns>
    private static bool IsLicenseFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        int slash = path.LastIndexOf('/');
        string fileName = slash < 0 ? path : path.Substring(slash + 1);
        return fileName.Equals("LICENSE", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("LICENSE.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The licence texts a model reports: every licence layer of a model created from a Modelfile or
    /// pulled from a registry, followed by the text of every licence file a bundle ships.
    /// </summary>
    /// <param name="layers">The decoded layers of the model.</param>
    /// <param name="cancellationToken">A token that cancels the reads.</param>
    /// <returns>The texts, in manifest order.</returns>
    /// <exception cref="ModelManagerException">A licence file's blob is not in the store.</exception>
    private async Task<IReadOnlyList<string>> ReadLicenseTextsAsync(
        ModelLayerReader layers, CancellationToken cancellationToken)
    {
        var fromFiles = new List<string>();
        foreach (ResolvedFile file in layers.BundleFiles)
        {
            if (!IsLicenseFileName(file.Name))
            {
                continue;
            }

            fromFiles.Add(await LayerFactory
                .ReadBlobTextAsync(_paths, file.Digest, cancellationToken).ConfigureAwait(false));
        }

        if (fromFiles.Count == 0)
        {
            return layers.LicensesOrEmpty();
        }

        var combined = new List<string>(layers.Licenses);
        combined.AddRange(fromFiles);
        return combined;
    }

    /// <summary>
    /// The licence a config layer states.
    /// </summary>
    /// <param name="config">The config layer.</param>
    /// <returns>
    /// The record, or <see cref="LicenseRecord.None"/> when the config states nothing, which is what
    /// every model pulled from an Ollama-protocol registry does.
    /// </returns>
    private static LicenseRecord ReadLicenseRecord(ModelConfig config)
    {
        string licenseId = ReadConfigProperty(config, ModelConfigKeys.LicenseId);
        string licenseSource = ReadConfigProperty(config, ModelConfigKeys.LicenseSource);
        return licenseId == null && licenseSource == null
            ? LicenseRecord.None
            : new LicenseRecord(licenseId, licenseSource, null);
    }

    /// <summary>
    /// The format of a model: what its config says, or "gguf" when it says nothing and the manifest
    /// carries GGUF weights.
    /// </summary>
    /// <param name="layers">The decoded layers of the model.</param>
    /// <returns>The format, or <see langword="null"/> when nothing says what it is.</returns>
    private static string ReadFormat(ModelLayerReader layers)
    {
        if (!string.IsNullOrEmpty(layers.Config.ModelFormat))
        {
            return layers.Config.ModelFormat;
        }
        return layers.ModelPath == null ? null : "gguf";
    }

    /// <summary>
    /// Reads one of the properties a bundle config records beside the ones this library models.
    /// </summary>
    /// <param name="config">The config layer.</param>
    /// <param name="propertyName">The property to read.</param>
    /// <returns>The value, or <see langword="null"/> when it is absent or is not a string.</returns>
    private static string ReadConfigProperty(ModelConfig config, string propertyName)
    {
        if (config == null || config.AdditionalProperties == null)
        {
            return null;
        }

        return config.AdditionalProperties.TryGetValue(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Reads the settings object a derived bundle's config records.
    /// </summary>
    /// <param name="config">The config layer.</param>
    /// <returns>
    /// The settings as strings, in the order they were written, or <see langword="null"/> when the
    /// config carries no settings object - which is every bundle this library did not derive itself.
    /// </returns>
    private static IReadOnlyDictionary<string, string> ReadConfigSettings(ModelConfig config)
    {
        if (config == null || config.AdditionalProperties == null
            || !config.AdditionalProperties.TryGetValue(ModelConfigKeys.Settings, out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            settings[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
        }

        return settings;
    }

    /// <summary>
    /// Turns a string into the JSON value a config property carries. A value that is not there is
    /// written as a JSON null rather than left out, so that a reader can tell "the source stated
    /// nothing" from "this config was written by something that did not know about the property".
    /// </summary>
    /// <param name="value">The value, which may be <see langword="null"/>.</param>
    /// <returns>The JSON value.</returns>
    private static JsonElement ToJsonElement(string value)
    {
        return JsonSerializer.SerializeToElement(value, ModelManagerJson.Options);
    }

    /// <summary>
    /// Inherits every layer and the config of a model already in the store, which is the port of
    /// Ollama's <c>parseFromModel</c>.
    /// </summary>
    /// <param name="argument">The FROM argument, as the Modelfile wrote it.</param>
    /// <param name="layers">The layer list the inherited layers are added to.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The config of the source model, which becomes the base config of the new one.</returns>
    /// <exception cref="ModelNotFoundException">The argument names neither a file nor a stored model.</exception>
    private async Task<ModelConfig> InheritFromModelAsync(
        string argument,
        List<ModelLayer> layers,
        CancellationToken cancellationToken)
    {
        ModelName sourceName = ModelName.Parse(argument, CreateNameDefaults());
        StoredManifest source = null;
        if (sourceName.IsValid)
        {
            source = await TryReadManifestAsync(sourceName, cancellationToken).ConfigureAwait(false);
        }
        if (source == null)
        {
            throw new ModelNotFoundException(argument, $"'{argument}' is neither a file nor a model in the store");
        }

        if (source.Manifest.Config == null || string.IsNullOrEmpty(source.Manifest.Config.Digest))
        {
            throw new ModelManagerException($"model {sourceName.DisplayShortest()} is missing its config");
        }

        ModelConfig config = await ModelLayerReader
            .ReadConfigAsync(_paths, source.Manifest, cancellationToken).ConfigureAwait(false);

        string from = sourceName.DisplayShortest();
        if (source.Manifest.Layers != null)
        {
            foreach (ModelLayer sourceLayer in source.Manifest.Layers)
            {
                if (sourceLayer == null || string.IsNullOrEmpty(sourceLayer.Digest))
                {
                    continue;
                }

                ModelLayer layer = await LayerFactory.CreateFromExistingBlobAsync(
                    _paths,
                    sourceLayer.Digest,
                    sourceLayer.MediaType,
                    from,
                    cancellationToken).ConfigureAwait(false);
                layer.Name = sourceLayer.Name;
                layers.Add(layer);
            }
        }

        return config;
    }

    /// <summary>
    /// Imports a GGUF file as a blob and returns the layer that names it, filling in the parts of the
    /// config a model layer decides. This is the port of Ollama's <c>ggufLayers</c> together with the
    /// config assignments of <c>createModel</c>.
    /// </summary>
    /// <param name="filePath">The resolved path of the file.</param>
    /// <param name="sourceLabel">
    /// What the layer records as its source: the FROM argument as the Modelfile wrote it, or the model a
    /// derived file came from.
    /// </param>
    /// <param name="config">The config being built.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The layer that names the imported file.</returns>
    /// <exception cref="GgufFormatException">The file is a LoRA adapter, which FROM cannot take.</exception>
    private async Task<ModelLayer> ImportGgufFileAsync(
        string filePath,
        string sourceLabel,
        ModelConfig config,
        CancellationToken cancellationToken)
    {
        GgufMetadata metadata = await GgufMetadata
            .ReadAsync(filePath, SingleElementReadOptions(), cancellationToken).ConfigureAwait(false);

        if (string.Equals(metadata.Kind, "adapter", StringComparison.Ordinal))
        {
            throw new GgufFormatException($"{filePath} is a LoRA adapter; use ADAPTER");
        }

        string mediaType = IsProjectorGguf(metadata) ? MediaTypes.Projector : MediaTypes.Model;
        ModelLayer layer = await LayerFactory
            .CreateFromFileAsync(_paths, filePath, mediaType, sourceLabel, cancellationToken).ConfigureAwait(false);

        if (string.Equals(mediaType, MediaTypes.Model, StringComparison.Ordinal))
        {
            string architecture = metadata.Architecture;
            config.ModelFormat = FirstNonEmpty(config.ModelFormat, "gguf");
            config.ModelFamily = FirstNonEmpty(config.ModelFamily, architecture);
            config.ModelType = FirstNonEmpty(config.ModelType, HumanFormat.HumanNumber(metadata.ParameterCount));
            config.FileType = FirstNonEmpty(config.FileType, metadata.FileTypeName);

            config.ModelFamilies ??= new List<string>();
            if (!config.ModelFamilies.Contains(architecture))
            {
                config.ModelFamilies.Add(architecture);
            }
        }

        return layer;
    }

    /// <summary>
    /// Overlays the Modelfile's own values on the layers gathered so far: a template, a system prompt
    /// and a messages list replace the inherited ones, parameters merge over the inherited ones and
    /// licenses are appended. This is the port of <c>ApplyModelfileLayers</c>.
    /// </summary>
    /// <param name="layers">The layer list, modified in place.</param>
    /// <param name="modelfile">The parsed Modelfile.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>A task that completes when every layer has been written.</returns>
    private async Task ApplyModelfileLayersAsync(
        List<ModelLayer> layers,
        Modelfile modelfile,
        CancellationToken cancellationToken)
    {
        if (modelfile.Template != null)
        {
            layers.RemoveAll(layer => IsMediaType(layer, MediaTypes.Prompt) || IsMediaType(layer, MediaTypes.Template));
            layers.Add(await LayerFactory
                .CreateFromTextAsync(_paths, modelfile.Template, MediaTypes.Template, cancellationToken)
                .ConfigureAwait(false));
        }

        if (modelfile.System != null)
        {
            layers.RemoveAll(layer => IsMediaType(layer, MediaTypes.System));
            layers.Add(await LayerFactory
                .CreateFromTextAsync(_paths, modelfile.System, MediaTypes.System, cancellationToken)
                .ConfigureAwait(false));
        }

        foreach (string license in modelfile.Licenses)
        {
            layers.Add(await LayerFactory
                .CreateFromTextAsync(_paths, license, MediaTypes.License, cancellationToken).ConfigureAwait(false));
        }

        if (modelfile.ParameterLines.Count > 0)
        {
            var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (ModelLayer layer in layers)
            {
                if (!IsMediaType(layer, MediaTypes.Params))
                {
                    continue;
                }

                byte[] inheritedBytes = await LayerFactory
                    .ReadBlobBytesAsync(_paths, layer.Digest, cancellationToken).ConfigureAwait(false);
                Dictionary<string, JsonElement> inherited =
                    ModelManagerJson.Deserialize<Dictionary<string, JsonElement>>(inheritedBytes);
                if (inherited == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, JsonElement> entry in inherited)
                {
                    if (!merged.ContainsKey(entry.Key))
                    {
                        merged[entry.Key] = entry.Value;
                    }
                }
            }

            // Serializing the typed parameters and reading them back as a plain object is how a new
            // value replaces an inherited one of the same name whole, as Go's maps.Copy does.
            byte[] parameterBytes = ModelManagerJson.SerializeLikeGo(modelfile.GetParameters());
            Dictionary<string, JsonElement> overlay =
                ModelManagerJson.Deserialize<Dictionary<string, JsonElement>>(parameterBytes);
            if (overlay != null)
            {
                foreach (KeyValuePair<string, JsonElement> entry in overlay)
                {
                    merged[entry.Key] = entry.Value;
                }
            }

            layers.RemoveAll(layer => IsMediaType(layer, MediaTypes.Params));
            layers.Add(await LayerFactory.CreateFromBytesAsync(
                _paths,
                ModelManagerJson.SerializeLikeGo(merged),
                MediaTypes.Params,
                cancellationToken).ConfigureAwait(false));
        }

        if (modelfile.Messages.Count > 0)
        {
            layers.RemoveAll(layer => IsMediaType(layer, MediaTypes.Messages));
            layers.Add(await LayerFactory.CreateFromBytesAsync(
                _paths,
                ModelManagerJson.SerializeLikeGo(new List<ModelMessage>(modelfile.Messages)),
                MediaTypes.Messages,
                cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Renders a model back to Modelfile text the way <c>ollama show --modelfile</c> does, with the
    /// blob paths in the FROM lines so that the text can be fed back to a create.
    /// </summary>
    /// <param name="layers">The decoded layers of the model.</param>
    /// <returns>The Modelfile text.</returns>
    private static string BuildModelfileText(ModelLayerReader layers)
    {
        var builder = new StringBuilder();
        foreach (ModelfileCommand command in BuildModelfileCommands(layers, layers.ModelPath))
        {
            builder.Append(command.ToString());
            builder.Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Every command that describes a model: its weights, its companions and everything a Modelfile says
    /// about how it is used.
    /// </summary>
    /// <param name="layers">The decoded layers of the model.</param>
    /// <param name="modelPath">
    /// The path the FROM line names. It is the model's own blob when a model is being rendered back to text,
    /// and a file just written when a derived model is being created from it.
    /// </param>
    /// <returns>The commands, in the order Ollama writes them.</returns>
    /// <remarks>
    /// Rendering a model as text and deriving a new model from it ask the same question - what does this
    /// model consist of - so they ask it in one place. A derived model that answered it differently would be
    /// a model that quietly lost its template or its stop parameters.
    /// </remarks>
    private static IReadOnlyList<ModelfileCommand> BuildModelfileCommands(
        ModelLayerReader layers, string modelPath)
    {
        var commands = new List<ModelfileCommand>();

        if (modelPath != null)
        {
            commands.Add(new ModelfileCommand("model", modelPath));
        }
        foreach (string adapter in layers.AdapterPaths)
        {
            commands.Add(new ModelfileCommand("adapter", adapter));
        }
        if (layers.DraftPath != null)
        {
            commands.Add(new ModelfileCommand("draft", layers.DraftPath));
        }

        // Upstream writes a projector as a further FROM line, so that reading the text back imports it
        // as another model argument.
        foreach (string projector in layers.ProjectorPaths)
        {
            commands.Add(new ModelfileCommand("model", projector));
        }

        if (layers.Template != null)
        {
            commands.Add(new ModelfileCommand("template", layers.Template));
        }
        if (layers.System != null)
        {
            commands.Add(new ModelfileCommand("system", layers.System));
        }
        if (!string.IsNullOrEmpty(layers.Config.Renderer))
        {
            commands.Add(new ModelfileCommand("renderer", layers.Config.Renderer));
        }
        if (!string.IsNullOrEmpty(layers.Config.Parser))
        {
            commands.Add(new ModelfileCommand("parser", layers.Config.Parser));
        }

        if (layers.ParameterValues != null)
        {
            foreach (KeyValuePair<string, JsonElement> parameter in layers.ParameterValues)
            {
                if (parameter.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement element in parameter.Value.EnumerateArray())
                    {
                        commands.Add(new ModelfileCommand(parameter.Key, FormatJsonValue(element)));
                    }
                    continue;
                }

                commands.Add(new ModelfileCommand(parameter.Key, FormatJsonValue(parameter.Value)));
            }
        }

        foreach (string license in layers.Licenses)
        {
            commands.Add(new ModelfileCommand("license", license));
        }
        foreach (ModelMessage message in layers.Messages)
        {
            commands.Add(new ModelfileCommand("message", message.Role + ": " + message.Content));
        }

        return commands;
    }

    /// <summary>
    /// Writes one JSON value the way a PARAMETER line spells it.
    /// </summary>
    /// <param name="element">The value.</param>
    /// <returns>The argument text.</returns>
    private static string FormatJsonValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return string.Empty;
            default:
                return element.GetRawText();
        }
    }

    /// <summary>
    /// Reports whether a GGUF file is a multimodal projector rather than a model, the port of Ollama's
    /// <c>isProjectorGGUF</c>.
    /// </summary>
    /// <param name="metadata">The metadata of the file.</param>
    /// <returns><see langword="true"/> when the file is a projector.</returns>
    private static bool IsProjectorGguf(GgufMetadata metadata)
    {
        string kind = metadata.Kind;
        if (string.Equals(kind, "projector", StringComparison.Ordinal)
            || string.Equals(kind, "mmproj", StringComparison.Ordinal))
        {
            return true;
        }

        // A file that counts vision blocks but no model blocks is a standalone vision tower.
        if (metadata.BlockCount == 0 && metadata.GetUInt64("vision.block_count") > 0)
        {
            return true;
        }

        return string.Equals(metadata.Architecture, "clip", StringComparison.Ordinal)
            && metadata.BlockCount == 0
            && (metadata.GetBool("has_vision_encoder") || metadata.GetBool("has_audio_encoder"));
    }

    /// <summary>
    /// The GGUF read options the create path uses: arrays are never retained, because nothing it reads
    /// needs a vocabulary.
    /// </summary>
    /// <returns>The read options.</returns>
    private static GgufReadOptions SingleElementReadOptions()
    {
        return new GgufReadOptions { MaxArraySize = 1 };
    }

    /// <summary>
    /// Returns the first value that is not null or empty, which is Go's <c>cmp.Or</c>.
    /// </summary>
    /// <param name="value">The value to prefer.</param>
    /// <param name="fallback">What to use when the first is empty.</param>
    /// <returns>The chosen value.</returns>
    private static string FirstNonEmpty(string value, string fallback)
    {
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <summary>
    /// Reports whether a layer carries a media type.
    /// </summary>
    /// <param name="layer">The layer to test.</param>
    /// <param name="mediaType">The media type to match.</param>
    /// <returns><see langword="true"/> when the layer carries that media type.</returns>
    private static bool IsMediaType(ModelLayer layer, string mediaType)
    {
        return layer != null && string.Equals(layer.MediaType, mediaType, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every digest a manifest names, its content layers followed by its config layer.
    /// </summary>
    /// <param name="manifest">The manifest to read.</param>
    /// <returns>The digests.</returns>
    private static IReadOnlyList<string> CollectDigests(ModelManifest manifest)
    {
        var digests = new List<string>();
        if (manifest.Layers != null)
        {
            foreach (ModelLayer layer in manifest.Layers)
            {
                if (layer != null && !string.IsNullOrEmpty(layer.Digest))
                {
                    digests.Add(layer.Digest);
                }
            }
        }
        if (manifest.Config != null && !string.IsNullOrEmpty(manifest.Config.Digest))
        {
            digests.Add(manifest.Config.Digest);
        }
        return digests;
    }

    /// <summary>
    /// Resolves a FROM or ADAPTER argument against the create base directory. A "~" is not expanded:
    /// the argument is a path, not a shell word.
    /// </summary>
    /// <param name="baseDirectory">The directory relative arguments are resolved against.</param>
    /// <param name="argument">The argument as the Modelfile wrote it.</param>
    /// <returns>The absolute path.</returns>
    private static string ResolveArgumentPath(string baseDirectory, string argument)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, argument));
        }
        catch (ArgumentException)
        {
            // An argument that is not a usable path is simply not a file, so it is left to be looked
            // up as a model name instead.
            return argument;
        }
    }

    /// <summary>
    /// The defaults a name string is completed with, taken from the store options.
    /// </summary>
    /// <returns>The defaults.</returns>
    private ModelName CreateNameDefaults()
    {
        return ModelName.CreateDefaults(
            _options.DefaultRegistryHost,
            _options.DefaultNamespace,
            _options.DefaultTag,
            ModelName.DefaultProtocolScheme);
    }

    /// <summary>
    /// Parses a name string and insists on a fully qualified result.
    /// </summary>
    /// <param name="name">The name string.</param>
    /// <param name="parameterName">The name of the argument the string came from.</param>
    /// <returns>The parsed name.</returns>
    /// <exception cref="ArgumentException">The string is null or blank.</exception>
    /// <exception cref="InvalidModelNameException">The string does not name a model.</exception>
    private ModelName ParseModelName(string name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A model name is required.", parameterName);
        }

        ModelName parsed = ModelName.Parse(name, CreateNameDefaults());
        if (!parsed.IsValid)
        {
            throw new InvalidModelNameException(name, "invalid model name");
        }
        return parsed;
    }

    /// <summary>
    /// Reads a manifest, treating an unreadable one as absent.
    /// </summary>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The manifest, or <see langword="null"/>.</returns>
    private async Task<StoredManifest> TryReadManifestAsync(ModelName name, CancellationToken cancellationToken)
    {
        try
        {
            return await ManifestFiles.ReadAsync(_paths, name, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelManagerException)
        {
            // A manifest that does not parse is replaced, not repaired, which is what Ollama does when
            // it pulls over a model whose manifest went bad.
            return null;
        }
    }

    /// <summary>
    /// Reads a manifest that has to be there.
    /// </summary>
    /// <param name="name">The fully qualified model name.</param>
    /// <param name="requestedName">The name string the caller wrote, for the error.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="ModelNotFoundException">The store has no such model.</exception>
    private async Task<StoredManifest> RequireManifestAsync(
        ModelName name,
        string requestedName,
        CancellationToken cancellationToken)
    {
        StoredManifest stored = await ManifestFiles
            .ReadAsync(_paths, name, cancellationToken).ConfigureAwait(false);
        if (stored == null)
        {
            throw new ModelNotFoundException(
                requestedName,
                string.Format(CultureInfo.InvariantCulture, "model '{0}' not found", name.DisplayShortest()));
        }
        return stored;
    }

    /// <summary>
    /// The one registry client of this store, created the first time a pull needs it.
    /// </summary>
    /// <returns>The registry client.</returns>
    private RegistryClient GetRegistryClient()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _registryClient ??= new RegistryClient(_options);
            return _registryClient;
        }
    }

    /// <summary>
    /// Throws when the store has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    //The two operations below are the doors through which this library reaches CodeBrix.Ollama.Core - the
    //reduction drives the ONNX codec and the conversion reads a SentencePiece model through the protobuf
    //reader - so they are where an application that installed mismatched CodeBrix.Ollama packages is told
    //so, in a sentence of its own rather than through a missing member somewhere further in. The check is
    //an integer comparison, and CoreContract.Revision is a constant this assembly's compiler baked in.
    private static void RequireCoreContract() =>
        CoreContract.Require(CoreContract.Revision, "CodeBrix.Ollama.ModelManager");
}
