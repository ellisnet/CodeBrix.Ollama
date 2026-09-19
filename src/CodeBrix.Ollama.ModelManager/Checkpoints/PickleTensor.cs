using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A tensor as a checkpoint's pickle describes it: a storage, where in that storage the values start, the
/// shape, and the strides that say how the shape is laid out over the storage.
/// </summary>
/// <remarks>
/// This is what <c>torch._utils._rebuild_tensor_v2</c> is allowed to produce. Nothing is read here: whether the
/// strides describe a contiguous tensor the reader can stream is decided by the reader, which refuses a
/// non-contiguous one by name.
/// </remarks>
internal sealed class PickleTensor
{
    internal PickleTensor(PickleStorage storage, long storageOffset, IReadOnlyList<long> shape,
        IReadOnlyList<long> strides)
    {
        Storage = storage;
        StorageOffset = storageOffset;
        Shape = shape;
        Strides = strides;
    }

    /// <summary>The storage the values live in.</summary>
    internal PickleStorage Storage { get; }

    /// <summary>The index of the first value, in values rather than bytes.</summary>
    internal long StorageOffset { get; }

    /// <summary>The dimensions, outermost first.</summary>
    internal IReadOnlyList<long> Shape { get; }

    /// <summary>The stride of each dimension, in values.</summary>
    internal IReadOnlyList<long> Strides { get; }
}
