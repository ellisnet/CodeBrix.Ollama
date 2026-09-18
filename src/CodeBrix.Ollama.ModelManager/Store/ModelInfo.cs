using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Everything <see cref="IModelStore.ShowAsync"/> can say about a model: the manifest, the config, every
/// text and JSON layer decoded, and the GGUF metadata of the model weights.
/// </summary>
public sealed class ModelInfo
{
    /// <summary>The fully qualified name.</summary>
    public ModelName Name { get; set; }

    /// <summary>The name in its shortest display form.</summary>
    public string DisplayName { get; set; }

    /// <summary>The sha256 of the manifest file as lowercase hex without a prefix.</summary>
    public string Digest { get; set; }

    /// <summary>The total size in bytes of every layer including the config.</summary>
    public long Size { get; set; }

    /// <summary>When the manifest file was last written.</summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>The manifest as read from disk.</summary>
    public ModelManifest Manifest { get; set; }

    /// <summary>The config layer contents, or an empty config when the manifest has none.</summary>
    public ModelConfig Config { get; set; }

    /// <summary>The template layer text, or <see langword="null"/> when the model has none.</summary>
    public string Template { get; set; }

    /// <summary>The system layer text, or <see langword="null"/> when the model has none.</summary>
    public string System { get; set; }

    /// <summary>The params layer decoded, or <see langword="null"/> when the model has none.</summary>
    public ModelParameters Parameters { get; set; }

    /// <summary>
    /// Every license layer's text, in manifest order, followed by the text of every <c>LICENSE</c> file
    /// a bundle ships; empty when there are none.
    /// </summary>
    public IReadOnlyList<string> Licenses { get; set; }

    /// <summary>
    /// What the source states about the licence: the identifier and the address it was read from, as
    /// the config layer recorded them when the model was pulled or imported.
    /// <see cref="LicenseRecord.None"/> when nothing is stated, which is also what a model pulled from
    /// an Ollama-protocol registry reports. Never <see langword="null"/>.
    /// </summary>
    public LicenseRecord License { get; set; }

    /// <summary>
    /// What the model's weights are: the config layer's model format, which is "gguf" for a model
    /// pulled from an Ollama-protocol registry and one of the bundle formats - "huggingface",
    /// "pytorch", "onnx", "tensorflow-checkpoint", "mixed", "files", "imported" - for a bundle.
    /// <see langword="null"/> when the config states none and no GGUF weights layer says otherwise.
    /// </summary>
    public string Format { get; set; }

    /// <summary>
    /// The model this one was DERIVED from - exported, or reduced - spelled exactly as that model is
    /// stored, or <see langword="null"/> when this bundle was not produced by this library from another
    /// model. Nothing checks that the source is still in the store: a derived bundle is a snapshot of
    /// what the source held when the tool ran.
    /// </summary>
    public string DerivedFrom { get; set; }

    /// <summary>
    /// The tool that produced a derived bundle - "onnxruntime-genai", "optimum" or "publisher" for files
    /// the publisher shipped and this library only registered - or <see langword="null"/> when the
    /// bundle was not derived.
    /// </summary>
    public string Tool { get; set; }

    /// <summary>
    /// The version of <see cref="Tool"/>, as the tool itself reported it, or <see langword="null"/>.
    /// </summary>
    public string ToolVersion { get; set; }

    /// <summary>
    /// The options the tool was run with, as strings, or <see langword="null"/> when the bundle was not
    /// derived. An export records its route, its precision and whether remote code was allowed.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; set; }

    /// <summary>The messages layer decoded; empty when there is none.</summary>
    public IReadOnlyList<ModelMessage> Messages { get; set; }

    /// <summary>The metadata of the first model-weights GGUF, or <see langword="null"/> when the model has no GGUF weights.</summary>
    public GgufMetadata Metadata { get; set; }

    /// <summary>The metadata of each projector GGUF, in manifest order; empty when there are none.</summary>
    public IReadOnlyList<GgufMetadata> ProjectorMetadata { get; set; }

    /// <summary>The capabilities inferred for the model.</summary>
    public IReadOnlyList<ModelCapability> Capabilities { get; set; }

    /// <summary>
    /// The model reconstructed as Modelfile text, the way <c>ollama show --modelfile</c> prints it.
    /// </summary>
    public string ModelfileText { get; set; }
}
