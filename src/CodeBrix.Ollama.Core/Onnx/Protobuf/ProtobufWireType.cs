namespace CodeBrix.Ollama.Core;

/// <summary>
/// The five wire types a Protocol Buffers field can use. The value is the low three bits of a field tag.
/// </summary>
internal enum ProtobufWireType
{
    /// <summary>A base-128 variable-length integer.</summary>
    Varint = 0,

    /// <summary>Eight little-endian bytes.</summary>
    Fixed64 = 1,

    /// <summary>A variable-length byte count followed by that many bytes.</summary>
    LengthDelimited = 2,

    /// <summary>The start of a deprecated group. Never written; rejected when read.</summary>
    StartGroup = 3,

    /// <summary>The end of a deprecated group. Never written; rejected when read.</summary>
    EndGroup = 4,

    /// <summary>Four little-endian bytes.</summary>
    Fixed32 = 5,
}
