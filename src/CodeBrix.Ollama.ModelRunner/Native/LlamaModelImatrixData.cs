using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// One tensor's importance-matrix row handed to the quantizer: <c>struct llama_model_imatrix_data</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaModelImatrixData
{
    /// <summary>The tensor's name, NUL-terminated UTF-8.</summary>
    public byte* Name;

    /// <summary>The importance values.</summary>
    public float* Data;

    /// <summary>How many values <see cref="Data"/> holds.</summary>
    public nuint Size;
}
