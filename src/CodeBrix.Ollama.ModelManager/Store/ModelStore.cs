// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: server/images.go, server/create.go, server/model.go and x/create/manifest.go at commit a43fad18.
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

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The default <see cref="IModelStore"/>: a local model store in Ollama's on-disk layout, fed from any
/// registry that speaks Ollama's manifest-and-blob protocol.
/// </summary>
public sealed class ModelStore : IModelStore, IDisposable
{
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
                        await RunPullAsync(parsed, channel.Writer, downloadToken).ConfigureAwait(false);
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
            Licenses = layers.LicensesOrEmpty(),
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
            Config = layers.Config
        };
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
    public async Task CreateAsync(
        string name,
        Modelfile modelfile,
        CreateOptions options = null,
        CancellationToken cancellationToken = default)
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

        var layers = new List<ModelLayer>();
        var config = new ModelConfig();

        // The first FROM argument decides where everything else comes from: a file on disk is imported
        // as a new blob, a name already in the store has its layers and config inherited.
        string firstArgument = modelfile.ModelArgs[0];
        string firstPath = ResolveArgumentPath(baseDirectory, firstArgument);
        if (File.Exists(firstPath))
        {
            layers.Add(await ImportGgufFileAsync(firstPath, firstArgument, config, cancellationToken)
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
            layers.Add(await ImportGgufFileAsync(path, argument, config, cancellationToken).ConfigureAwait(false));
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
    /// <param name="argument">The FROM argument, as the Modelfile wrote it, recorded on the layer.</param>
    /// <param name="config">The config being built.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The layer that names the imported file.</returns>
    /// <exception cref="GgufFormatException">The file is a LoRA adapter, which FROM cannot take.</exception>
    private async Task<ModelLayer> ImportGgufFileAsync(
        string filePath,
        string argument,
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
            .CreateFromFileAsync(_paths, filePath, mediaType, argument, cancellationToken).ConfigureAwait(false);

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
        var commands = new List<ModelfileCommand>();

        if (layers.ModelPath != null)
        {
            commands.Add(new ModelfileCommand("model", layers.ModelPath));
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

        var builder = new StringBuilder();
        foreach (ModelfileCommand command in commands)
        {
            builder.Append(command.ToString());
            builder.Append('\n');
        }
        return builder.ToString();
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
}
