namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// How a model is turned into ONNX. Three of the four are real routes; the fourth picks between them.
/// </summary>
public enum ExportRoute
{
    /// <summary>
    /// Choose a route from what the model actually holds: a bundle that already ships <c>.onnx</c> files
    /// takes <see cref="PublisherOnnx"/>, a checkpoint whose <c>config.json</c> names an architecture the
    /// ONNX Runtime GenAI model builder writes takes <see cref="GenAiBuilder"/>, and anything else takes
    /// <see cref="Optimum"/>. This is the default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// A PASS-THROUGH of the <c>.onnx</c> files the publisher already shipped, with the files that go
    /// with them. Nothing is converted and no Python is needed or started: the store is content
    /// addressed, so the derived bundle names the blobs the source bundle already holds.
    /// </summary>
    PublisherOnnx = 1,

    /// <summary>
    /// The ONNX Runtime GenAI model builder (<c>onnxruntime_genai.models.builder</c>), which writes a
    /// <c>model.onnx</c> and the <c>genai_config.json</c> beside it for the transformer architectures it
    /// supports. It needs Python with <c>onnxruntime_genai</c>, <c>torch</c>, <c>transformers</c> and
    /// <c>onnx</c> installed.
    /// </summary>
    GenAiBuilder = 2,

    /// <summary>
    /// Hugging Face Optimum's ONNX exporter (<c>optimum.exporters.onnx.main_export</c>), which traces the
    /// model's own Python code. It is slower and more general than the builder, and is the fallback for a
    /// model the builder does not write. It needs Python with <c>optimum</c>, <c>onnx</c>, <c>torch</c>
    /// and <c>transformers</c> installed.
    /// </summary>
    Optimum = 3
}
