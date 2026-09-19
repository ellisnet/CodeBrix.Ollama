using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One tensor queued for writing: its GGUF name, its ggml type, its shape in GGUF order and the callback that
/// produces its values when the writer reaches them.
/// </summary>
internal sealed class GgufWriterTensor
{
    internal GgufWriterTensor(string name, GgufTensorType type, IReadOnlyList<long> shape, long byteCount,
        GgufTensorDataWriter writeData)
    {
        Name = name;
        Type = type;
        Shape = shape;
        ByteCount = byteCount;
        WriteData = writeData;
    }

    /// <summary>The tensor name, for example <c>blk.0.attn_q.weight</c>.</summary>
    internal string Name { get; }

    /// <summary>The ggml type the values are written as.</summary>
    internal GgufTensorType Type { get; }

    /// <summary>The dimensions in GGUF order, fastest moving first.</summary>
    internal IReadOnlyList<long> Shape { get; }

    /// <summary>How many bytes the values occupy, before the padding that follows them.</summary>
    internal long ByteCount { get; }

    /// <summary>Produces the values when the writer reaches this tensor.</summary>
    internal GgufTensorDataWriter WriteData { get; }
}
