using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// Sends the tensors whose names match a pattern to a particular buffer type:
/// <c>struct llama_model_tensor_buft_override</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaModelTensorBuftOverride
{
    /// <summary>The tensor-name pattern, NUL-terminated UTF-8.</summary>
    public byte* Pattern;

    /// <summary>The <c>ggml_backend_buffer_type_t</c> those tensors go to.</summary>
    public void* Buft;
}
