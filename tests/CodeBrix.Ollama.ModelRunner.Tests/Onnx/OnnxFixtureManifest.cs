namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One fixture case's <c>case.json</c>: what the graph takes, what ONNX Runtime made of it, and any note the
/// generator left about why the case is there.
/// </summary>
public sealed class OnnxFixtureManifest
{
    /// <summary>The case's name, which is also its folder's name.</summary>
    public string Name { get; set; }

    /// <summary>The operator-set version the graph is written against.</summary>
    public int Opset { get; set; }

    /// <summary>The tensors a run must be given.</summary>
    public OnnxFixtureTensor[] Inputs { get; set; }

    /// <summary>The tensors ONNX Runtime produced.</summary>
    public OnnxFixtureTensor[] Outputs { get; set; }

    /// <summary>What the case is for, when the generator said; otherwise <see langword="null"/>.</summary>
    public string Note { get; set; }

    /// <summary>
    /// How far this case's numbers may be from ONNX Runtime's, when the ordinary bar does not apply to it;
    /// otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// There is exactly one reason for a case to carry its own: an operator whose attributes ASK ONNX Runtime
    /// to compute in lower precision than this engine does. <c>MatMulNBits</c> with an accuracy level of four
    /// is the case - it lets a runtime quantize the activations to 8-bit integers as well, which this engine
    /// deliberately does not - and the difference that follows is the plan's tolerance for a quantized graph,
    /// not a defect. Every other case is held to the strict bar.
    /// </remarks>
    public double? Tolerance { get; set; }
}
