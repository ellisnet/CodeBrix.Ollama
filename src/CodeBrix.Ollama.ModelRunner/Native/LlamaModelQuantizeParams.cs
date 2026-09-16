using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// How <c>llama_model_quantize</c> rewrites a model: <c>struct llama_model_quantize_params</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaModelQuantizeParams
{
    /// <summary>The threads to quantize with; zero or less means one per hardware thread.</summary>
    public int NThread;

    /// <summary>The file type to write.</summary>
    public LlamaFtype Ftype;

    /// <summary>The element type of the output tensor.</summary>
    public GgmlType OutputTensorType;

    /// <summary>The element type of the token-embedding tensor.</summary>
    public GgmlType TokenEmbeddingType;

    /// <summary>Non-zero to allow re-quantizing tensors that are not 32- or 16-bit float.</summary>
    public byte AllowRequantize;

    /// <summary>Non-zero to quantize the output tensor as well.</summary>
    public byte QuantizeOutputTensor;

    /// <summary>Non-zero to copy tensors unchanged, ignoring the type settings.</summary>
    public byte OnlyCopy;

    /// <summary>Non-zero to write every tensor at the default type.</summary>
    public byte Pure;

    /// <summary>Non-zero to keep the same number of file shards.</summary>
    public byte KeepSplit;

    /// <summary>Non-zero to report the final size without writing anything.</summary>
    public byte DryRun;

    /// <summary>The importance-matrix rows, or null.</summary>
    public LlamaModelImatrixData* Imatrix;

    /// <summary>Metadata overrides to write, or null.</summary>
    public LlamaModelKvOverride* KvOverrides;

    /// <summary>Per-tensor type overrides, or null.</summary>
    public LlamaModelTensorOverride* TtOverrides;

    /// <summary>The indices of layers to drop, or null.</summary>
    public int* PruneLayers;
}
