using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Writes one tensor's values into the GGUF file being built.
/// </summary>
/// <remarks>
/// The writer calls this when it reaches the tensor's place in the data section, so the values are produced
/// while they are written rather than held: a converter reads the checkpoint through a stream, converts a
/// block at a time and writes it on. It must write exactly the byte count the tensor was declared with.
/// </remarks>
/// <param name="destination">The stream to write the values to.</param>
/// <param name="cancellationToken">A token that cancels the write.</param>
/// <returns>A task that completes when the values have been written.</returns>
internal delegate Task GgufTensorDataWriter(Stream destination, CancellationToken cancellationToken);
