namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama fs/gguf/gguf.go;

/// <summary>
/// The type tag that a GGUF file stores beside every key-value, in the numeric order the format defines.
/// </summary>
public enum GgufValueType
{
    /// <summary>An unsigned 8-bit integer.</summary>
    UInt8 = 0,

    /// <summary>A signed 8-bit integer.</summary>
    Int8 = 1,

    /// <summary>An unsigned 16-bit integer.</summary>
    UInt16 = 2,

    /// <summary>A signed 16-bit integer.</summary>
    Int16 = 3,

    /// <summary>An unsigned 32-bit integer.</summary>
    UInt32 = 4,

    /// <summary>A signed 32-bit integer.</summary>
    Int32 = 5,

    /// <summary>A 32-bit IEEE 754 floating point number.</summary>
    Float32 = 6,

    /// <summary>A boolean stored in one byte.</summary>
    Bool = 7,

    /// <summary>A UTF-8 string with a leading length.</summary>
    String = 8,

    /// <summary>An array of values that all share one element type.</summary>
    Array = 9,

    /// <summary>An unsigned 64-bit integer.</summary>
    UInt64 = 10,

    /// <summary>A signed 64-bit integer.</summary>
    Int64 = 11,

    /// <summary>A 64-bit IEEE 754 floating point number.</summary>
    Float64 = 12
}
