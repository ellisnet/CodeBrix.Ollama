using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Builds a complete fake model - a small GGUF, the text and JSON layers that go with it, a config and
/// a manifest - and registers all of it in a <see cref="FakeRegistryHandler"/>, so that a store test
/// can pull a model that looks exactly like a real one without touching the network.
/// </summary>
public sealed class FakeModelBuilder
{
    private readonly List<string> _licenses = new List<string> { "MIT" };

    /// <summary>
    /// Creates a builder for one model name.
    /// </summary>
    /// <param name="namespace">The namespace part, "library" by default.</param>
    /// <param name="model">The model part, "test" by default.</param>
    /// <param name="tag">The tag part, "latest" by default.</param>
    public FakeModelBuilder(string @namespace = "library", string model = "test", string tag = "latest")
    {
        Namespace = @namespace;
        Model = model;
        Tag = tag;
    }

    /// <summary>The namespace part of the name.</summary>
    public string Namespace { get; }

    /// <summary>The model part of the name.</summary>
    public string Model { get; }

    /// <summary>The tag part of the name.</summary>
    public string Tag { get; }

    /// <summary>The host to put in front of the reference, or <see langword="null"/> for none.</summary>
    public string Host { get; set; }

    /// <summary>The <c>general.architecture</c> of the model GGUF.</summary>
    public string Architecture { get; set; } = "llama";

    /// <summary>The <c>general.file_type</c> of the model GGUF; 7 is Q8_0.</summary>
    public uint FileType { get; set; } = 7;

    /// <summary>The architecture's <c>block_count</c>.</summary>
    public uint BlockCount { get; set; } = 2;

    /// <summary>The architecture's <c>context_length</c>.</summary>
    public uint ContextLength { get; set; } = 2048;

    /// <summary>The architecture's <c>embedding_length</c>.</summary>
    public uint EmbeddingLength { get; set; } = 64;

    /// <summary>
    /// The <c>tokenizer.chat_template</c> of the model GGUF. The default mentions tools, so a model
    /// built with it reports the tools capability.
    /// </summary>
    public string ChatTemplate { get; set; } = "{% if tools %}{{ tools }}{% endif %}{{ messages }}";

    /// <summary>
    /// A value written as <c>general.name</c>, which is how two fake models are given different model
    /// blobs and therefore different digests.
    /// </summary>
    public string Variant { get; set; }

    /// <summary>How many bytes of filler the model GGUF carries, to make it worth splitting.</summary>
    public int FillerBytes { get; set; } = 1200;

    /// <summary>The Go template layer text, or <see langword="null"/> for no template layer.</summary>
    public string Template { get; set; } = "{{ .System }}\nUser: {{ .Prompt }}\nAssistant: ";

    /// <summary>The system layer text, or <see langword="null"/> for no system layer.</summary>
    public string SystemPrompt { get; set; } = "You are a test model.";

    /// <summary>The params layer JSON, or <see langword="null"/> for no params layer.</summary>
    public string ParametersJson { get; set; } = "{\"stop\":[\"</s>\"],\"temperature\":0.5}";

    /// <summary>The messages layer JSON, or <see langword="null"/> for no messages layer.</summary>
    public string MessagesJson { get; set; } =
        "[{\"role\":\"user\",\"content\":\"hi\"},{\"role\":\"assistant\",\"content\":\"hello\"}]";

    /// <summary>The license layer texts. Defaults to a single "MIT" layer.</summary>
    public IList<string> Licenses
    {
        get { return _licenses; }
    }

    /// <summary>Whether the model carries a vision projector layer.</summary>
    public bool IncludeProjector { get; set; }

    /// <summary>Whether the manifest carries a safetensors tensor layer, which cannot be pulled.</summary>
    public bool IncludeTensorLayer { get; set; }

    /// <summary>The config layer JSON, or <see langword="null"/> for a manifest with no config.</summary>
    public string ConfigJson { get; set; }

    /// <summary>The bytes of the model GGUF, available after <see cref="Build"/>.</summary>
    public byte[] ModelBytes { get; private set; }

    /// <summary>The bytes of the projector GGUF, available after <see cref="Build"/>.</summary>
    public byte[] ProjectorBytes { get; private set; }

    /// <summary>The exact manifest bytes the fake registry serves, available after <see cref="Build"/>.</summary>
    public byte[] ManifestBytes { get; private set; }

    /// <summary>The manifest the fake registry serves, available after <see cref="Build"/>.</summary>
    public ModelManifest Manifest { get; private set; }

    /// <summary>The digest of the model layer, available after <see cref="Build"/>.</summary>
    public string ModelDigest { get; private set; }

    /// <summary>The digest of the projector layer, available after <see cref="Build"/>.</summary>
    public string ProjectorDigest { get; private set; }

    /// <summary>The digest of the template layer, available after <see cref="Build"/>.</summary>
    public string TemplateDigest { get; private set; }

    /// <summary>The digest of the system layer, available after <see cref="Build"/>.</summary>
    public string SystemDigest { get; private set; }

    /// <summary>The digest of the params layer, available after <see cref="Build"/>.</summary>
    public string ParametersDigest { get; private set; }

    /// <summary>The digest of the messages layer, available after <see cref="Build"/>.</summary>
    public string MessagesDigest { get; private set; }

    /// <summary>The digest of the config layer, available after <see cref="Build"/>.</summary>
    public string ConfigDigest { get; private set; }

    /// <summary>The repository the fake registry files the manifest under, as "namespace/model".</summary>
    public string Repository
    {
        get { return Namespace + "/" + Model; }
    }

    /// <summary>
    /// The name string a test pulls: the host when one is set, the namespace when it is not "library",
    /// then the model and the tag.
    /// </summary>
    public string Reference
    {
        get
        {
            if (!string.IsNullOrEmpty(Host))
            {
                return Host + "/" + Namespace + "/" + Model + ":" + Tag;
            }
            if (!string.Equals(Namespace, "library", StringComparison.Ordinal))
            {
                return Namespace + "/" + Model + ":" + Tag;
            }
            return Model + ":" + Tag;
        }
    }

    /// <summary>
    /// Builds the model GGUF bytes without registering anything, which is what a create test writes to
    /// a file on disk.
    /// </summary>
    /// <returns>The GGUF file bytes.</returns>
    public byte[] BuildModelGguf()
    {
        var builder = new GgufTestFileBuilder()
            .AddString("general.architecture", Architecture)
            .AddString("general.name", Variant ?? Model)
            .AddUInt32("general.file_type", FileType)
            .AddUInt32(Architecture + ".block_count", BlockCount)
            .AddUInt32(Architecture + ".context_length", ContextLength)
            .AddUInt32(Architecture + ".embedding_length", EmbeddingLength);

        if (ChatTemplate != null)
        {
            builder.AddString("tokenizer.chat_template", ChatTemplate);
        }
        if (FillerBytes > 0)
        {
            builder.AddString("general.description", new string('x', FillerBytes));
        }

        builder.AddTensor("token_embd.weight", GgufTensorType.F32, 8, 8)
            .AddTensor("blk.0.attn_q.weight", GgufTensorType.F32, 8, 8)
            .AddTensor("output.weight", GgufTensorType.F32, 8, 8);

        return builder.Build();
    }

    /// <summary>
    /// Builds the projector GGUF bytes without registering anything.
    /// </summary>
    /// <returns>The GGUF file bytes.</returns>
    public byte[] BuildProjectorGguf()
    {
        return new GgufTestFileBuilder()
            .AddString("general.architecture", "clip")
            .AddString("general.type", "projector")
            .AddUInt32("clip.vision.block_count", 2)
            .AddBool("clip.has_vision_encoder", true)
            .AddTensor("v.blk.0.attn_q.weight", GgufTensorType.F32, 4, 4)
            .Build();
    }

    /// <summary>
    /// Builds every blob and the manifest and registers them in the fake registry.
    /// </summary>
    /// <param name="handler">The fake registry to register with.</param>
    /// <returns>The manifest that was registered.</returns>
    public ModelManifest Build(FakeRegistryHandler handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var layers = new List<ModelLayer>();

        ModelBytes = BuildModelGguf();
        ModelDigest = AddLayer(handler, layers, MediaTypes.Model, ModelBytes);

        if (IncludeProjector)
        {
            ProjectorBytes = BuildProjectorGguf();
            ProjectorDigest = AddLayer(handler, layers, MediaTypes.Projector, ProjectorBytes);
        }

        if (IncludeTensorLayer)
        {
            AddLayer(handler, layers, MediaTypes.Tensor, Encoding.UTF8.GetBytes("not really a tensor"));
        }

        if (Template != null)
        {
            TemplateDigest = AddLayer(handler, layers, MediaTypes.Template, Encoding.UTF8.GetBytes(Template));
        }
        if (SystemPrompt != null)
        {
            SystemDigest = AddLayer(handler, layers, MediaTypes.System, Encoding.UTF8.GetBytes(SystemPrompt));
        }
        if (ParametersJson != null)
        {
            ParametersDigest = AddLayer(handler, layers, MediaTypes.Params, Encoding.UTF8.GetBytes(ParametersJson));
        }
        if (MessagesJson != null)
        {
            MessagesDigest = AddLayer(handler, layers, MediaTypes.Messages, Encoding.UTF8.GetBytes(MessagesJson));
        }
        foreach (string license in _licenses)
        {
            AddLayer(handler, layers, MediaTypes.License, Encoding.UTF8.GetBytes(license));
        }

        var manifest = new ModelManifest { Layers = layers };

        string configJson = ConfigJson ?? DefaultConfigJson();
        if (configJson != null)
        {
            byte[] configBytes = Encoding.UTF8.GetBytes(configJson);
            ConfigDigest = FakeRegistryHandler.ComputeDigest(configBytes);
            handler.AddBlob(ConfigDigest, configBytes);
            manifest.Config = new ModelLayer(MediaTypes.Config, ConfigDigest, configBytes.Length);
        }

        ManifestBytes = ModelManagerJson.SerializeLikeGo(manifest);
        handler.AddManifest(Repository, Tag, ManifestBytes);
        Manifest = manifest;
        return manifest;
    }

    /// <summary>
    /// The config the model carries when none was supplied.
    /// </summary>
    /// <returns>The config JSON.</returns>
    private string DefaultConfigJson()
    {
        return "{\"model_format\":\"gguf\",\"model_family\":\"" + Architecture
            + "\",\"model_families\":[\"" + Architecture
            + "\"],\"model_type\":\"192\",\"file_type\":\"Q8_0\"}";
    }

    /// <summary>
    /// Registers one blob and records the layer that names it.
    /// </summary>
    /// <param name="handler">The fake registry.</param>
    /// <param name="layers">The layer list being built.</param>
    /// <param name="mediaType">The media type of the layer.</param>
    /// <param name="content">The blob bytes.</param>
    /// <returns>The digest of the blob.</returns>
    private static string AddLayer(
        FakeRegistryHandler handler,
        List<ModelLayer> layers,
        string mediaType,
        byte[] content)
    {
        string digest = FakeRegistryHandler.ComputeDigest(content);
        handler.AddBlob(digest, content);
        layers.Add(new ModelLayer(mediaType, digest, content.Length));
        return digest;
    }
}
