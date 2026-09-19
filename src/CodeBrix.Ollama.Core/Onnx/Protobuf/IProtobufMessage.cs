namespace CodeBrix.Ollama.Core;

/// <summary>
/// One Protocol Buffers message in the ONNX schema. Serialization runs in two passes: <see cref="CalculateSize"/>
/// measures the message and every message below it and remembers each result in <see cref="CachedSize"/>, then
/// <see cref="WriteTo"/> emits the bytes using those remembered lengths.
/// </summary>
internal interface IProtobufMessage
{
    /// <summary>The length this message last measured, in bytes.</summary>
    int CachedSize { get; }

    /// <summary>Measures this message and every message below it, remembering each result.</summary>
    /// <returns>The encoded length of this message body, in bytes.</returns>
    int CalculateSize();

    /// <summary>Writes this message body, using the lengths remembered by the size pass.</summary>
    /// <param name="writer">The writer to append to.</param>
    void WriteTo(ProtobufWriter writer);
}
