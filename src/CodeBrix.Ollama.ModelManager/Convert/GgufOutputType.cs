namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The numeric type a conversion writes a model's weights as.
/// </summary>
/// <remarks>
/// Whatever is chosen here, the tensors that the inference engine needs at full precision are always written as
/// 32-bit floats: every one-dimensional tensor, and every normalization weight.
/// </remarks>
public enum GgufOutputType
{
    /// <summary>
    /// Keep the checkpoint's own type: a bfloat16 checkpoint becomes BF16, a float16 checkpoint F16, and
    /// anything else F16. The type is decided from the weights themselves, not from what the configuration says.
    /// </summary>
    Auto = 0,

    /// <summary>Write every weight as a 32-bit float. The largest and most faithful output.</summary>
    F32 = 1,

    /// <summary>Write the weights as 16-bit floats.</summary>
    F16 = 2,

    /// <summary>Write the weights as bfloat16, the type most transformer checkpoints are published in.</summary>
    BF16 = 3
}
