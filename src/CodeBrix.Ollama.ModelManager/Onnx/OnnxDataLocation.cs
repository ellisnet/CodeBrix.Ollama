namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Where an ONNX <c>TensorProto</c> keeps its bytes.
/// </summary>
internal enum OnnxDataLocation
{
    /// <summary>The bytes are inside the model file, in the tensor's own data fields.</summary>
    Default = 0,

    /// <summary>The bytes are in a side file named by the tensor's external-data entries.</summary>
    External = 1,
}
