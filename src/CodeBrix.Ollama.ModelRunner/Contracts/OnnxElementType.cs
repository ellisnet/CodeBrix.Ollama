namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The element types an <see cref="OnnxTensor"/> can carry, numbered as the ONNX specification numbers them.
/// </summary>
/// <remarks>
/// <para>
/// These are the types the managed interpreter computes in. A graph whose inputs, outputs or initializers
/// declare anything else is refused when it is loaded, naming the tensor and the type, with one exception: a
/// 16-bit float initializer is widened to <see cref="Float"/> as the model is read, because that is a storage
/// format rather than a compute format.
/// </para>
/// <para>
/// <see cref="UInt8"/> and <see cref="Int8"/> are the QUANTIZED types, and they are different from the rest:
/// they live only INSIDE a quantized graph - an activation between <c>DynamicQuantizeLinear</c> and
/// <c>MatMulInteger</c>, or a weight a kernel folded when the model was loaded - and an
/// <see cref="OnnxTensor"/> does not carry them, so a graph that declares one as its own input or output is
/// refused when it is loaded, naming the tensor and saying why.
/// </para>
/// </remarks>
public enum OnnxElementType
{
    /// <summary>32-bit IEEE 754 floating point, which is what the engine computes in.</summary>
    Float = 1,

    /// <summary>Unsigned 8-bit integer: a quantized activation or weight, inside a quantized graph only.</summary>
    UInt8 = 2,

    /// <summary>Signed 8-bit integer: a quantized weight, inside a quantized graph only.</summary>
    Int8 = 3,

    /// <summary>Signed 32-bit integer.</summary>
    Int32 = 6,

    /// <summary>Signed 64-bit integer, which is what shapes, indices and token identifiers use.</summary>
    Int64 = 7,

    /// <summary>A boolean, one to an element.</summary>
    Bool = 9,
}
