using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One tensor as a checkpoint container declares it: the name the publisher gave it, its element type, its
/// shape in the publisher's order (outermost dimension first) and how many bytes its values occupy.
/// </summary>
/// <remarks>
/// The values are never read while the container is enumerated. <see cref="ICheckpointReader.OpenTensorAsync"/>
/// opens a stream over exactly the <see cref="ByteCount"/> bytes this descriptor names.
/// </remarks>
internal sealed class CheckpointTensor
{
    internal CheckpointTensor(string name, CheckpointDataType dataType, IReadOnlyList<long> shape, long byteOffset)
    {
        Name = name;
        DataType = dataType;
        Shape = shape;
        ByteOffset = byteOffset;
        ElementCount = CheckpointDataTypes.ElementCount(shape, "tensor \"" + name + "\"");
        ByteCount = ElementCount * CheckpointDataTypes.GetByteWidth(dataType);
    }

    /// <summary>The name the checkpoint gives the tensor, for example <c>model.embed_tokens.weight</c>.</summary>
    internal string Name { get; }

    /// <summary>The element type of the tensor's values.</summary>
    internal CheckpointDataType DataType { get; }

    /// <summary>The dimensions, outermost first, as the publisher wrote them.</summary>
    internal IReadOnlyList<long> Shape { get; }

    /// <summary>The number of values.</summary>
    internal long ElementCount { get; }

    /// <summary>The number of bytes the values occupy.</summary>
    internal long ByteCount { get; }

    /// <summary>Where the values begin, in the units the owning reader uses.</summary>
    internal long ByteOffset { get; }

    /// <summary>Returns the tensor name.</summary>
    /// <returns>The value of <see cref="Name"/>.</returns>
    public override string ToString()
    {
        return Name ?? string.Empty;
    }
}
