using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// Quantizes the tensors whose names match a pattern to a particular type:
/// <c>struct llama_model_tensor_override</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaModelTensorOverride
{
    /// <summary>The tensor-name pattern, NUL-terminated UTF-8.</summary>
    public byte* Pattern;

    /// <summary>The element type those tensors are written as.</summary>
    public GgmlType Type;
}
