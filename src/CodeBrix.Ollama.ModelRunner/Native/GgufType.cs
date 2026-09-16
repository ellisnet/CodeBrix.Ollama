namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/gguf.h;


/// <summary>
/// The type of a value stored in a GGUF key/value pair. Mirrors <c>enum gguf_type</c>.
/// </summary>
internal enum GgufType : int
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

    /// <summary>A 32-bit float.</summary>
    Float32 = 6,

    /// <summary>A boolean, stored as one byte.</summary>
    Bool = 7,

    /// <summary>A length-prefixed string.</summary>
    String = 8,

    /// <summary>An array of one of the other types.</summary>
    Array = 9,

    /// <summary>An unsigned 64-bit integer.</summary>
    UInt64 = 10,

    /// <summary>A signed 64-bit integer.</summary>
    Int64 = 11,

    /// <summary>A 64-bit float.</summary>
    Float64 = 12,

    /// <summary>The number of defined types; not a type itself.</summary>
    Count = 13,
}
