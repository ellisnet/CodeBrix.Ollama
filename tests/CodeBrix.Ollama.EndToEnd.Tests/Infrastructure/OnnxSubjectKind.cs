namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// What a subject's graph does to its numbers, which is what settles how closely two implementations of it
/// can be expected to agree.
/// </summary>
public enum OnnxSubjectKind
{
    /// <summary>
    /// Nothing is quantized. Two implementations differ only in the order they add floats up in, so they
    /// agree to about a millionth and the bar is a ten-thousandth.
    /// </summary>
    FullPrecision = 0,

    /// <summary>
    /// The WEIGHTS were quantized and the activations were not. The quantization is the same arithmetic on
    /// both sides - the same packed bytes, the same scales - so the two still agree to about a millionth,
    /// and the plan's bar of a hundredth is comfortable.
    /// </summary>
    QuantizedWeights = 1,

    /// <summary>
    /// The ACTIVATIONS are quantized as the graph runs, through <c>DynamicQuantizeLinear</c>. A tensor's
    /// scale is taken from its own largest and smallest element, so a difference in the last bit of ONE
    /// element moves the scale and with it every value that was halfway between two integers - and a dozen
    /// layers multiply that up. Two faithful implementations can then differ by a few percent, and so can one
    /// implementation's two arithmetic paths.
    /// </summary>
    QuantizedActivations = 2,

    /// <summary>
    /// The graph ASKS onnxruntime to compute in lower precision than this engine does - <c>MatMulNBits</c>
    /// with an accuracy level of four, which lets a runtime quantize the activations to 8-bit integers as
    /// well.
    /// </summary>
    /// <remarks>
    /// This engine reads the attribute and keeps the activations in floats, which is MORE accurate than
    /// anything the attribute permits, so the two answers differ by a few percent and the greedy choice can
    /// differ with them. It is not a difference in the operator: the same export with the attribute stripped
    /// out, and nothing else changed, agrees to 2.9e-6 with no greedy choice differing at all. The greedy
    /// choice is therefore reported for a subject of this kind rather than required to match.
    /// </remarks>
    LowerPrecisionByRequest = 3,
}
