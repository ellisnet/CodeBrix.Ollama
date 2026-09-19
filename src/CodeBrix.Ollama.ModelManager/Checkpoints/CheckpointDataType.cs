namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The element types a checkpoint container may declare for a tensor. The names are the ones safetensors uses;
/// the PyTorch storage classes map on to the same set.
/// </summary>
internal enum CheckpointDataType
{
    /// <summary>A 64-bit IEEE 754 floating point number.</summary>
    F64,

    /// <summary>A 32-bit IEEE 754 floating point number.</summary>
    F32,

    /// <summary>A 16-bit IEEE 754 floating point number.</summary>
    F16,

    /// <summary>A bfloat16: the top sixteen bits of a 32-bit float.</summary>
    BF16,

    /// <summary>A signed 64-bit integer.</summary>
    I64,

    /// <summary>A signed 32-bit integer.</summary>
    I32,

    /// <summary>A signed 16-bit integer.</summary>
    I16,

    /// <summary>A signed 8-bit integer.</summary>
    I8,

    /// <summary>An unsigned 8-bit integer.</summary>
    U8,

    /// <summary>A boolean stored in one byte.</summary>
    Bool
}
