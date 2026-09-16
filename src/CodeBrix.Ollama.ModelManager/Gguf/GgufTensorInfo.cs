// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/tensor.go at commit a43fad18.
using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One tensor descriptor from a GGUF file: its name, shape, type and offset into the tensor data section.
/// The tensor's values are never read.
/// </summary>
public sealed class GgufTensorInfo
{
    private static readonly ulong[] EmptyShape = Array.Empty<ulong>();

    internal GgufTensorInfo(string name, ulong offset, ulong[] shape, GgufTensorType type)
    {
        Name = name;
        Offset = offset;
        Shape = shape ?? EmptyShape;
        Type = type;
        ElementCount = ComputeElementCount(Shape);
        ByteCount = ComputeByteCount(Shape, type, ElementCount);
    }

    /// <summary>The tensor name, for example <c>blk.0.attn_q.weight</c>.</summary>
    public string Name { get; }

    /// <summary>The tensor's offset in bytes from the start of the tensor data section.</summary>
    public ulong Offset { get; }

    /// <summary>The dimensions, fastest moving first. A GGUF tensor has at most four.</summary>
    public IReadOnlyList<ulong> Shape { get; }

    /// <summary>The ggml type of the tensor's values.</summary>
    public GgufTensorType Type { get; }

    /// <summary>The number of values, or -1 when the shape overflows a signed 64-bit count.</summary>
    public long ElementCount { get; }

    /// <summary>
    /// The number of bytes the tensor occupies, or -1 when the type has no defined size, the first dimension
    /// is not a whole number of blocks, or the count overflows.
    /// </summary>
    public long ByteCount { get; }

    /// <summary>
    /// <see langword="true"/> when the tensor has a name and a usable byte count.
    /// </summary>
    public bool IsValid
    {
        get { return !string.IsNullOrEmpty(Name) && ByteCount > 0; }
    }

    /// <summary>The lowercase ggml name of <see cref="Type"/>.</summary>
    public string TypeName
    {
        get { return GgufTensorTypes.GetName(Type); }
    }

    /// <summary>Returns the tensor name.</summary>
    /// <returns>The value of <see cref="Name"/>.</returns>
    public override string ToString()
    {
        return Name ?? string.Empty;
    }

    private static long ComputeElementCount(IReadOnlyList<ulong> shape)
    {
        long count = 1;
        for (int i = 0; i < shape.Count; i++)
        {
            ulong dimension = shape[i];
            if (dimension > long.MaxValue)
            {
                return -1;
            }

            long n = (long)dimension;
            if (n != 0 && count > long.MaxValue / n)
            {
                return -1;
            }

            count *= n;
        }

        return count;
    }

    private static long ComputeByteCount(IReadOnlyList<ulong> shape, GgufTensorType type, long elementCount)
    {
        if (elementCount < 0)
        {
            return -1;
        }

        long typeSize = GgufTensorTypes.GetTypeSize(type);
        long blockSize = GgufTensorTypes.GetBlockSize(type);
        if (typeSize == 0 || blockSize == 0)
        {
            return -1;
        }

        long rowSize = 1;
        if (shape.Count > 0)
        {
            if (shape[0] > long.MaxValue)
            {
                return -1;
            }

            rowSize = (long)shape[0];
        }

        if (rowSize % blockSize != 0)
        {
            return -1;
        }

        long blocks = elementCount / blockSize;
        if (blocks > long.MaxValue / typeSize)
        {
            return -1;
        }

        return blocks * typeSize;
    }
}
