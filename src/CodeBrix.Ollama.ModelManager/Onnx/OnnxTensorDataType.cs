namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The element types an ONNX <c>TensorProto</c> can declare, with the numbers the schema assigns them.
/// </summary>
internal enum OnnxTensorDataType
{
    /// <summary>No type declared.</summary>
    Undefined = 0,

    /// <summary>32-bit IEEE 754 floating point.</summary>
    Float = 1,

    /// <summary>Unsigned 8-bit integer.</summary>
    UInt8 = 2,

    /// <summary>Signed 8-bit integer.</summary>
    Int8 = 3,

    /// <summary>Unsigned 16-bit integer.</summary>
    UInt16 = 4,

    /// <summary>Signed 16-bit integer.</summary>
    Int16 = 5,

    /// <summary>Signed 32-bit integer.</summary>
    Int32 = 6,

    /// <summary>Signed 64-bit integer.</summary>
    Int64 = 7,

    /// <summary>UTF-8 text.</summary>
    String = 8,

    /// <summary>A boolean stored in one byte.</summary>
    Bool = 9,

    /// <summary>16-bit IEEE 754 floating point.</summary>
    Float16 = 10,

    /// <summary>64-bit IEEE 754 floating point.</summary>
    Double = 11,

    /// <summary>Unsigned 32-bit integer.</summary>
    UInt32 = 12,

    /// <summary>Unsigned 64-bit integer.</summary>
    UInt64 = 13,

    /// <summary>A complex number of two 32-bit floats.</summary>
    Complex64 = 14,

    /// <summary>A complex number of two 64-bit floats.</summary>
    Complex128 = 15,

    /// <summary>The truncated 16-bit form of a 32-bit float.</summary>
    BFloat16 = 16,

    /// <summary>8-bit float with four exponent bits, NaN but no infinity.</summary>
    Float8E4M3Fn = 17,

    /// <summary>8-bit float with four exponent bits, no negative zero.</summary>
    Float8E4M3FnUz = 18,

    /// <summary>8-bit float with five exponent bits.</summary>
    Float8E5M2 = 19,

    /// <summary>8-bit float with five exponent bits, no negative zero.</summary>
    Float8E5M2FnUz = 20,

    /// <summary>Unsigned 4-bit integer, two to a byte.</summary>
    UInt4 = 21,

    /// <summary>Signed 4-bit integer, two to a byte.</summary>
    Int4 = 22,

    /// <summary>4-bit float with two exponent bits.</summary>
    Float4E2M1 = 23,

    /// <summary>8-bit exponent-only float, the scale of microscaling formats.</summary>
    Float8E8M0 = 24,

    /// <summary>Unsigned 2-bit integer, four to a byte.</summary>
    UInt2 = 25,

    /// <summary>Signed 2-bit integer, four to a byte.</summary>
    Int2 = 26,
}
