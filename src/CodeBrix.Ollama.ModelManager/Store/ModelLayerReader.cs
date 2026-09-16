// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: server/images.go at commit a43fad18.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One manifest's layers decoded: the blob path of every file layer and the contents of every text and
/// JSON layer. This is the layer walk of Ollama's <c>GetModel</c>, shared by describing a model and by
/// resolving one, so the two can never disagree about what a manifest holds.
/// </summary>
internal sealed class ModelLayerReader
{
    private static readonly IReadOnlyList<string> NoStrings = Array.Empty<string>();
    private static readonly IReadOnlyList<ModelMessage> NoMessages = Array.Empty<ModelMessage>();

    private readonly List<string> _modelShardPaths = new List<string>();
    private readonly List<string> _projectorPaths = new List<string>();
    private readonly List<string> _projectorDigests = new List<string>();
    private readonly List<string> _adapterPaths = new List<string>();
    private readonly List<string> _licenses = new List<string>();

    private ModelLayerReader()
    {
    }

    /// <summary>
    /// The config layer contents, or an empty config when the manifest has none or it cannot be read.
    /// </summary>
    public ModelConfig Config { get; private set; } = new ModelConfig();

    /// <summary>
    /// The absolute path of the first model-weights blob, or <see langword="null"/> when there is none.
    /// </summary>
    public string ModelPath { get; private set; }

    /// <summary>
    /// The digest of the first model-weights blob, or <see langword="null"/> when there is none.
    /// </summary>
    public string ModelDigest { get; private set; }

    /// <summary>
    /// The absolute paths of any further model-weights blobs of a split model, in manifest order.
    /// </summary>
    public IReadOnlyList<string> ModelShardPaths
    {
        get { return _modelShardPaths; }
    }

    /// <summary>
    /// The absolute paths of the projector blobs, in manifest order.
    /// </summary>
    public IReadOnlyList<string> ProjectorPaths
    {
        get { return _projectorPaths; }
    }

    /// <summary>
    /// The digests of the projector blobs, in the same order as <see cref="ProjectorPaths"/>.
    /// </summary>
    public IReadOnlyList<string> ProjectorDigests
    {
        get { return _projectorDigests; }
    }

    /// <summary>
    /// The absolute paths of the adapter blobs, in manifest order.
    /// </summary>
    public IReadOnlyList<string> AdapterPaths
    {
        get { return _adapterPaths; }
    }

    /// <summary>
    /// The absolute path of the draft-model blob, or <see langword="null"/> when there is none.
    /// </summary>
    public string DraftPath { get; private set; }

    /// <summary>
    /// The template layer text, taken from either the template or the older prompt layer, or
    /// <see langword="null"/> when the model has neither.
    /// </summary>
    public string Template { get; private set; }

    /// <summary>
    /// The system layer text, or <see langword="null"/> when the model has none.
    /// </summary>
    public string System { get; private set; }

    /// <summary>
    /// The params layer decoded into the typed parameters, or <see langword="null"/> when the model
    /// has no params layer.
    /// </summary>
    public ModelParameters Parameters { get; private set; }

    /// <summary>
    /// The params layer decoded as the plain JSON object it is, in document order, or
    /// <see langword="null"/> when the model has no params layer. This is what Ollama keeps in
    /// <c>Model.Options</c> and what a Modelfile's PARAMETER lines are rendered from.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> ParameterValues { get; private set; }

    /// <summary>
    /// Every license layer's text, in manifest order.
    /// </summary>
    public IReadOnlyList<string> Licenses
    {
        get { return _licenses; }
    }

    /// <summary>
    /// The messages layer decoded, or an empty list when the model has none.
    /// </summary>
    public IReadOnlyList<ModelMessage> Messages { get; private set; } = NoMessages;

    /// <summary>
    /// Walks a manifest's layers, resolving blob paths and decoding the text and JSON layers.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="manifest">The manifest to walk.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The decoded layers.</returns>
    /// <exception cref="ModelManagerException">A layer names a blob that is not in the store.</exception>
    public static async Task<ModelLayerReader> ReadAsync(
        ModelStorePaths paths,
        ModelManifest manifest,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var reader = new ModelLayerReader();
        reader.Config = await ReadConfigAsync(paths, manifest, cancellationToken).ConfigureAwait(false);

        if (manifest.Layers == null)
        {
            return reader;
        }

        foreach (ModelLayer layer in manifest.Layers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (layer == null || string.IsNullOrEmpty(layer.Digest))
            {
                continue;
            }

            // Nothing below reads a safetensors tensor layer, and resolving its path would cost a
            // check for every one of the hundreds such a model carries.
            if (string.Equals(layer.MediaType, MediaTypes.Tensor, StringComparison.Ordinal))
            {
                continue;
            }

            string blobPath = paths.GetBlobPath(layer.Digest);
            switch (layer.MediaType)
            {
                case MediaTypes.Model:
                    if (reader.ModelPath == null)
                    {
                        reader.ModelPath = blobPath;
                        reader.ModelDigest = layer.Digest;
                    }
                    else
                    {
                        reader._modelShardPaths.Add(blobPath);
                    }
                    break;
                case MediaTypes.Draft:
                    reader.DraftPath ??= blobPath;
                    break;
                case MediaTypes.Projector:
                    reader._projectorPaths.Add(blobPath);
                    reader._projectorDigests.Add(layer.Digest);
                    break;
                case MediaTypes.Adapter:
                    reader._adapterPaths.Add(blobPath);
                    break;
                case MediaTypes.Prompt:
                case MediaTypes.Template:
                    reader.Template = await LayerFactory
                        .ReadBlobTextAsync(paths, layer.Digest, cancellationToken).ConfigureAwait(false);
                    break;
                case MediaTypes.System:
                    reader.System = await LayerFactory
                        .ReadBlobTextAsync(paths, layer.Digest, cancellationToken).ConfigureAwait(false);
                    break;
                case MediaTypes.Params:
                    await reader.ReadParametersAsync(paths, layer.Digest, cancellationToken).ConfigureAwait(false);
                    break;
                case MediaTypes.Messages:
                    await reader.ReadMessagesAsync(paths, layer.Digest, cancellationToken).ConfigureAwait(false);
                    break;
                case MediaTypes.License:
                    reader._licenses.Add(await LayerFactory
                        .ReadBlobTextAsync(paths, layer.Digest, cancellationToken).ConfigureAwait(false));
                    break;
                default:
                    // Deprecated embeddings layers and anything a newer Ollama adds are carried in the
                    // manifest but not interpreted here.
                    break;
            }
        }

        return reader;
    }

    /// <summary>
    /// Reads and decodes the config layer, treating a missing or unreadable config as an empty one so
    /// that a model whose config blob was lost can still be listed and removed.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="manifest">The manifest whose config to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The config, never <see langword="null"/>.</returns>
    public static async Task<ModelConfig> ReadConfigAsync(
        ModelStorePaths paths,
        ModelManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest == null || manifest.Config == null || string.IsNullOrEmpty(manifest.Config.Digest))
        {
            return new ModelConfig();
        }

        try
        {
            byte[] bytes = await LayerFactory
                .ReadBlobBytesAsync(paths, manifest.Config.Digest, cancellationToken).ConfigureAwait(false);
            return ModelManagerJson.Deserialize<ModelConfig>(bytes) ?? new ModelConfig();
        }
        catch (ModelManagerException)
        {
            return new ModelConfig();
        }
        catch (JsonException)
        {
            return new ModelConfig();
        }
    }

    /// <summary>
    /// Reads the params layer both as typed parameters and as the raw JSON object.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="digest">The digest of the params blob.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>A task that completes when the layer has been decoded.</returns>
    private async Task ReadParametersAsync(ModelStorePaths paths, string digest, CancellationToken cancellationToken)
    {
        byte[] bytes = await LayerFactory.ReadBlobBytesAsync(paths, digest, cancellationToken).ConfigureAwait(false);
        try
        {
            Parameters = ModelManagerJson.Deserialize<ModelParameters>(bytes);
            ParameterValues = ModelManagerJson.Deserialize<Dictionary<string, JsonElement>>(bytes);
        }
        catch (JsonException exception)
        {
            throw new ModelManagerException($"params layer {digest} is not valid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Reads the messages layer.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="digest">The digest of the messages blob.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>A task that completes when the layer has been decoded.</returns>
    private async Task ReadMessagesAsync(ModelStorePaths paths, string digest, CancellationToken cancellationToken)
    {
        byte[] bytes = await LayerFactory.ReadBlobBytesAsync(paths, digest, cancellationToken).ConfigureAwait(false);
        try
        {
            Messages = ModelManagerJson.Deserialize<List<ModelMessage>>(bytes) ?? new List<ModelMessage>();
        }
        catch (JsonException exception)
        {
            throw new ModelManagerException($"messages layer {digest} is not valid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The licenses as a list that is never <see langword="null"/>.
    /// </summary>
    /// <returns>The license texts.</returns>
    public IReadOnlyList<string> LicensesOrEmpty()
    {
        return _licenses.Count == 0 ? NoStrings : _licenses;
    }
}
