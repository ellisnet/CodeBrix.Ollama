using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/gguf.h;

/// <summary>
/// How a GGUF file is opened: <c>struct gguf_init_params</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GgufInitParams
{
    /// <summary>Non-zero to read the metadata only and never allocate tensor data.</summary>
    public byte NoAlloc;

    /// <summary>
    /// When not null, receives a <c>struct ggml_context *</c> holding the tensor data. Pass null to read
    /// metadata alone.
    /// </summary>
    public void** Ctx;
}
