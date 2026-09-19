namespace CodeBrix.Ollama.Core;

/// <summary>
/// How <see cref="OnnxModel"/> writes a model file: in one piece, or with its larger tensors moved to a side file the
/// way ONNX's external-data convention describes.
/// </summary>
internal sealed class OnnxSaveOptions
{
    /// <summary>
    /// Whether to move tensors out to a side file. When <see langword="false"/> every tensor keeps its bytes inside
    /// the model file and any side-file reference is resolved first.
    /// </summary>
    internal bool UseExternalData { get; set; }

    /// <summary>
    /// The side file's name, relative to the model file's own folder. When left <see langword="null"/> the model
    /// file's name with <c>.data</c> appended is used, which is what ONNX's own tooling writes.
    /// </summary>
    internal string ExternalDataFileName { get; set; }

    /// <summary>
    /// The smallest tensor, in bytes, that is moved out to the side file. Tensors below it stay in the model file.
    /// ONNX's own tooling uses 1024 by default.
    /// </summary>
    internal int SizeThreshold { get; set; } = 1024;
}
