// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/tensor.go at commit a43fad18.
namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The ggml tensor type ids that a GGUF tensor descriptor can carry. The numbers are the ggml type ids and
/// never change; several of them are legacy or unquantizable types that no current writer emits.
/// </summary>
public enum GgufTensorType : uint
{
    /// <summary>32-bit float, one value per block.</summary>
    F32 = 0,

    /// <summary>16-bit float, one value per block.</summary>
    F16 = 1,

    /// <summary>4-bit quantization with one scale per block of 32.</summary>
    Q4_0 = 2,

    /// <summary>4-bit quantization with a scale and a minimum per block of 32.</summary>
    Q4_1 = 3,

    /// <summary>A withdrawn 4-bit quantization; no size is defined for it.</summary>
    Q4_2 = 4,

    /// <summary>A withdrawn 4-bit quantization; no size is defined for it.</summary>
    Q4_3 = 5,

    /// <summary>5-bit quantization with one scale per block of 32.</summary>
    Q5_0 = 6,

    /// <summary>5-bit quantization with a scale and a minimum per block of 32.</summary>
    Q5_1 = 7,

    /// <summary>8-bit quantization with one scale per block of 32.</summary>
    Q8_0 = 8,

    /// <summary>8-bit quantization with a scale and a sum per block of 32.</summary>
    Q8_1 = 9,

    /// <summary>2-bit k-quantization.</summary>
    Q2_K = 10,

    /// <summary>3-bit k-quantization.</summary>
    Q3_K = 11,

    /// <summary>4-bit k-quantization.</summary>
    Q4_K = 12,

    /// <summary>5-bit k-quantization.</summary>
    Q5_K = 13,

    /// <summary>6-bit k-quantization.</summary>
    Q6_K = 14,

    /// <summary>8-bit k-quantization.</summary>
    Q8_K = 15,

    /// <summary>2-bit importance quantization, extra extra small.</summary>
    IQ2_XXS = 16,

    /// <summary>2-bit importance quantization, extra small.</summary>
    IQ2_XS = 17,

    /// <summary>3-bit importance quantization, extra extra small.</summary>
    IQ3_XXS = 18,

    /// <summary>1-bit importance quantization, small.</summary>
    IQ1_S = 19,

    /// <summary>4-bit importance quantization, non-linear.</summary>
    IQ4_NL = 20,

    /// <summary>3-bit importance quantization, small.</summary>
    IQ3_S = 21,

    /// <summary>2-bit importance quantization, small.</summary>
    IQ2_S = 22,

    /// <summary>4-bit importance quantization, extra small.</summary>
    IQ4_XS = 23,

    /// <summary>A signed 8-bit integer per value.</summary>
    I8 = 24,

    /// <summary>A signed 16-bit integer per value.</summary>
    I16 = 25,

    /// <summary>A signed 32-bit integer per value.</summary>
    I32 = 26,

    /// <summary>A signed 64-bit integer per value.</summary>
    I64 = 27,

    /// <summary>64-bit float, one value per block.</summary>
    F64 = 28,

    /// <summary>1-bit importance quantization, medium.</summary>
    IQ1_M = 29,

    /// <summary>bfloat16, one value per block.</summary>
    BF16 = 30,

    /// <summary>A withdrawn repacked Q4_0 layout; no size is defined for it.</summary>
    Q4_0_4_4 = 31,

    /// <summary>A withdrawn repacked Q4_0 layout; no size is defined for it.</summary>
    Q4_0_4_8 = 32,

    /// <summary>A withdrawn repacked Q4_0 layout; no size is defined for it.</summary>
    Q4_0_8_8 = 33,

    /// <summary>A ternary quantization; no size is defined for it here.</summary>
    TQ1_0 = 34,

    /// <summary>A ternary quantization; no size is defined for it here.</summary>
    TQ2_0 = 35,

    /// <summary>A withdrawn repacked IQ4_NL layout; no size is defined for it.</summary>
    IQ4_NL_4_4 = 36,

    /// <summary>A withdrawn repacked IQ4_NL layout; no size is defined for it.</summary>
    IQ4_NL_4_8 = 37,

    /// <summary>A withdrawn repacked IQ4_NL layout; no size is defined for it.</summary>
    IQ4_NL_8_8 = 38,

    /// <summary>Microscaling 4-bit float with one shared exponent per block of 32.</summary>
    MXFP4 = 39,

    /// <summary>NVIDIA 4-bit float with one scale per block of 64.</summary>
    NVFP4 = 40,

    /// <summary>1-bit quantization with one scale per block of 128.</summary>
    Q1_0 = 41
}
