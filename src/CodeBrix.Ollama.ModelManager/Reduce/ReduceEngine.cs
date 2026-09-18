namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Which implementation does the arithmetic of a reduction. Two of the three are real engines; the third
/// picks between them.
/// </summary>
public enum ReduceEngine
{
    /// <summary>
    /// Let this library choose. The two weight-only modes always take <see cref="Managed"/>, so making an
    /// existing graph smaller needs nothing installed; dynamic quantization takes <see cref="Managed"/>
    /// when the graph has already been prepared and <see cref="Python"/> when it has not, because
    /// preparing one runs there; and preparation itself is always <see cref="Python"/>. This is the
    /// default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// ONNX Runtime's own quantization tools, run in a CPython this machine has: the reference
    /// implementation, and the only engine for a mode the managed one does not cover. It needs Python
    /// with <c>onnx</c> and <c>onnxruntime</c> installed.
    /// </summary>
    Python = 1,

    /// <summary>
    /// This library's own managed implementation, which needs no Python and no native library: an ONNX
    /// codec and a port of ONNX Runtime's quantizers, writing the same file those tools write. It covers
    /// the two weight-only modes and dynamic eight-bit quantization of a prepared graph, and refuses what
    /// it does not implement rather than approximating it.
    /// </summary>
    Managed = 2
}
