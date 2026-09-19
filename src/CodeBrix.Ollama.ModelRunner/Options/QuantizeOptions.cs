namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// How a GGUF model is rewritten at another quantization. Every default is the one the inference engine
/// itself uses, so a quantization asked for with no options at all - or with none of these properties set -
/// produces the file the engine's own quantizer produces.
/// </summary>
/// <remarks>
/// <para>
/// The engine's quantizer has more switches than these three: an importance matrix and the tensor patterns
/// that go with it, per-tensor and per-layer type overrides, metadata overrides, layer pruning, a dry run,
/// leaving the output tensor alone, and writing the result as the same number of shards the source had. They
/// are deliberately not here. Each of them either needs a file format of its own, or changes what the output
/// of one call IS - the shard switch makes the engine write a numbered SET of files rather than the one file
/// this API promises - and a consumer who needs them is a consumer who wants the engine's own tool.
/// </para>
/// <para>
/// WHAT IS NOT SETTABLE IS AT THE ENGINE'S DEFAULT, not at zero: the output tensor is quantized, no
/// importance matrix is supplied, no type is overridden, nothing is pruned and the result is one file.
/// </para>
/// </remarks>
public sealed class QuantizeOptions
{
    /// <summary>
    /// How many threads quantize the tensors. Zero - the default - lets the engine use one per hardware
    /// thread, which is what its own tool does when it is given no thread count; so does any negative number.
    /// </summary>
    /// <remarks>
    /// It changes how long the work takes and nothing else: each thread quantizes whole rows of its own, so
    /// the bytes written do not depend on how many threads wrote them.
    /// </remarks>
    public int Threads { get; set; }

    /// <summary>
    /// Whether a tensor that is ALREADY quantized may be quantized again. The default is
    /// <see langword="false"/>, which is the engine's own default, and it means a source whose weights are
    /// not 32- or 16-bit float is refused rather than degraded.
    /// </summary>
    /// <remarks>
    /// Quantizing twice loses far more than quantizing once: the second pass reads the first pass's rounded
    /// values as if they were the model's. Set this only when the source really is the only file there is.
    /// </remarks>
    public bool AllowRequantize { get; set; }

    /// <summary>
    /// Whether every tensor is written at the type <see cref="GgufQuantizationType"/> names, rather than at
    /// the engine's mixture. The default is <see langword="false"/>, which is the engine's own default.
    /// </summary>
    /// <remarks>
    /// The k-quantizations are mixtures on purpose: the engine writes the tensors that matter most at the
    /// next type up, which is the whole difference between a K_S and a K_M file. Setting this turns the
    /// mixture off, which makes the file smaller and the model measurably worse.
    /// </remarks>
    public bool Pure { get; set; }
}
