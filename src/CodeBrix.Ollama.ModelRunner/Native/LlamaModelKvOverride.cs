using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// One metadata key/value pair replaced at load time: <c>struct llama_model_kv_override</c>.
/// </summary>
/// <remarks>
/// <para>
/// The C structure ends in an anonymous union, so this one uses an explicit layout. The union starts at
/// offset 136: the tag occupies the first four bytes, the 128-byte key follows it at offset 4, and the union
/// needs eight-byte alignment for its <c>int64_t</c> and <c>double</c> members, which pushes it past the 132
/// bytes already used. That makes the whole structure 264 bytes, which the layout test in the test
/// project checks against the library.
/// </para>
/// <para>Both character arrays hold NUL-terminated UTF-8 and are fixed 128-byte buffers, not pointers.</para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 264)]
internal unsafe struct LlamaModelKvOverride
{
    /// <summary>Which member of the union carries the value.</summary>
    [FieldOffset(0)]
    public LlamaModelKvOverrideType Tag;

    /// <summary>The metadata key, at most 127 bytes and its terminator.</summary>
    [FieldOffset(4)]
    public fixed byte Key[128];

    /// <summary>The value when <see cref="Tag"/> is <see cref="LlamaModelKvOverrideType.Int"/>.</summary>
    [FieldOffset(136)]
    public long ValI64;

    /// <summary>The value when <see cref="Tag"/> is <see cref="LlamaModelKvOverrideType.Float"/>.</summary>
    [FieldOffset(136)]
    public double ValF64;

    /// <summary>The value when <see cref="Tag"/> is <see cref="LlamaModelKvOverrideType.Bool"/>.</summary>
    [FieldOffset(136)]
    public byte ValBool;

    /// <summary>The value when <see cref="Tag"/> is <see cref="LlamaModelKvOverrideType.Str"/>.</summary>
    [FieldOffset(136)]
    public fixed byte ValStr[128];
}
