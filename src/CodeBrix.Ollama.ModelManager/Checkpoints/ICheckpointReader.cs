using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One checkpoint container, opened for reading: it lists the tensors it holds in its own order and opens a
/// stream over one tensor's bytes at a time.
/// </summary>
/// <remarks>
/// Nothing here reads a whole checkpoint into memory - a container of any size is read one tensor at a time,
/// and a tensor is read through a stream rather than an array. A reader that composes several files (the
/// sharded containers a later phase adds) implements this same interface over the parts it holds.
/// </remarks>
internal interface ICheckpointReader : IDisposable
{
    /// <summary>The tensors the container declares, in the order the container declares them.</summary>
    IReadOnlyList<CheckpointTensor> Tensors { get; }

    /// <summary>Opens a stream over one tensor's values.</summary>
    /// <param name="tensor">A descriptor this reader returned from <see cref="Tensors"/>.</param>
    /// <param name="cancellationToken">A token that cancels the open.</param>
    /// <returns>A stream of exactly <see cref="CheckpointTensor.ByteCount"/> bytes, which the caller disposes.</returns>
    Task<Stream> OpenTensorAsync(CheckpointTensor tensor, CancellationToken cancellationToken = default);
}
