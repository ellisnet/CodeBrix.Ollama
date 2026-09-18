namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a reduction does to an exported graph. Three modes make the file smaller in different ways, and
/// the fourth only prepares a graph for one of them.
/// </summary>
public enum ReduceMode
{
    /// <summary>
    /// DYNAMIC INT8: every constant weight of a <c>MatMul</c> becomes eight-bit, and the activations are
    /// quantized while the model runs, which is where the word dynamic comes from. The graph gains
    /// <c>MatMulInteger</c> nodes and the scales that go with them, and a weight matrix takes a quarter
    /// of the space it took as single precision. A <c>Gemm</c> that can become a <c>MatMul</c> is
    /// rewritten into one first, so it is covered as well.
    /// </summary>
    DynamicInt8 = 0,

    /// <summary>
    /// WEIGHT-ONLY INT8, block-wise: the constant weight of every <c>MatMul</c> is split into blocks
    /// along its rows and each block is stored as eight-bit values with a scale of its own, in a
    /// <c>MatMulNBits</c> node. Nothing about the activations changes: they stay in floating point and
    /// the weights are widened again as the model runs.
    /// </summary>
    WeightOnlyInt8 = 1,

    /// <summary>
    /// WEIGHT-ONLY INT4, block-wise: the same thing as <see cref="WeightOnlyInt8"/> with four bits per
    /// value, which is the smallest of these modes and the one that changes the numbers most. It needs a
    /// runtime that implements <c>MatMulNBits</c>.
    /// </summary>
    WeightOnlyInt4 = 2,

    /// <summary>
    /// PREPROCESS ONLY: shape inference and the basic graph optimizations that a quantizer wants to see,
    /// stored as a derived bundle of its own and quantized by nobody. It is what makes the same prepared
    /// graph available to more than one quantizer, so that what they produce can be compared.
    /// </summary>
    PreprocessOnly = 3
}
