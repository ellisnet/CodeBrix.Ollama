using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Reads a GGUF file and writes a quantized copy of it. The caller supplies one of these; this library has
/// no quantizer of its own and never will.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE SEAM, AND IT IS A DELIBERATE ONE. Quantizing weights is the inference engine's work, and the
/// package that carries that engine - CodeBrix.Ollama.ModelRunner - is a package this one does not reference
/// and must never reference: a store that could not be used without a native inference engine would be a
/// different kind of thing altogether. So the store does what only a store can do - find the file, name the
/// result, record where it came from, put it away - and asks the CONSUMER for the one step it cannot do.
/// </para>
/// <para>
/// An implementation must write the file at <paramref name="outputPath"/> and nothing else. It is given a
/// path in a directory of the store's own making, and the store removes that directory however the work ends.
/// </para>
/// </remarks>
/// <param name="inputPath">The full path of the GGUF file to read. It must not be modified.</param>
/// <param name="outputPath">The full path of the quantized file to write.</param>
/// <param name="cancellationToken">A token that cancels the work.</param>
/// <returns>A task that completes when the quantized file has been written.</returns>
public delegate Task GgufQuantizer(string inputPath, string outputPath, CancellationToken cancellationToken);
