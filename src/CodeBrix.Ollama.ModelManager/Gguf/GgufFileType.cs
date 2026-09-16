// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/file_type.go at commit a43fad18.
namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The value of the <c>general.file_type</c> key: the quantization the model as a whole was written with.
/// The numbers are the llama.cpp file type ids and never change.
/// </summary>
public enum GgufFileType : uint
{
    /// <summary>Every tensor is 32-bit float.</summary>
    F32 = 0,

    /// <summary>Mostly 16-bit float.</summary>
    F16 = 1,

    /// <summary>Mostly Q4_0.</summary>
    Q4_0 = 2,

    /// <summary>Mostly Q4_1.</summary>
    Q4_1 = 3,

    /// <summary>A withdrawn mix of Q4_1 and 16-bit float; it has no name.</summary>
    Q4_1F16 = 4,

    /// <summary>A withdrawn file type; it has no name.</summary>
    Q4_2 = 5,

    /// <summary>A withdrawn file type; it has no name.</summary>
    Q4_3 = 6,

    /// <summary>Mostly Q8_0.</summary>
    Q8_0 = 7,

    /// <summary>Mostly Q5_0.</summary>
    Q5_0 = 8,

    /// <summary>Mostly Q5_1.</summary>
    Q5_1 = 9,

    /// <summary>Mostly Q2_K.</summary>
    Q2K = 10,

    /// <summary>Mostly Q3_K, small.</summary>
    Q3KS = 11,

    /// <summary>Mostly Q3_K, medium.</summary>
    Q3KM = 12,

    /// <summary>Mostly Q3_K, large.</summary>
    Q3KL = 13,

    /// <summary>Mostly Q4_K, small.</summary>
    Q4_K_S = 14,

    /// <summary>Mostly Q4_K, medium.</summary>
    Q4_K_M = 15,

    /// <summary>Mostly Q5_K, small.</summary>
    Q5KS = 16,

    /// <summary>Mostly Q5_K, medium.</summary>
    Q5KM = 17,

    /// <summary>Mostly Q6_K.</summary>
    Q6K = 18,

    /// <summary>Mostly IQ2_XXS.</summary>
    IQ2XXS = 19,

    /// <summary>Mostly IQ2_XS.</summary>
    IQ2XS = 20,

    /// <summary>Mostly Q2_K, small.</summary>
    Q2KS = 21,

    /// <summary>Mostly IQ3_XS.</summary>
    IQ3XS = 22,

    /// <summary>Mostly IQ3_XXS.</summary>
    IQ3XXS = 23,

    /// <summary>Mostly IQ1_S.</summary>
    IQ1S = 24,

    /// <summary>Mostly IQ4_NL.</summary>
    IQ4NL = 25,

    /// <summary>Mostly IQ3_S.</summary>
    IQ3S = 26,

    /// <summary>Mostly IQ3_M.</summary>
    IQ3M = 27,

    /// <summary>Mostly IQ2_S.</summary>
    IQ2S = 28,

    /// <summary>Mostly IQ2_M.</summary>
    IQ2M = 29,

    /// <summary>Mostly IQ4_XS.</summary>
    IQ4XS = 30,

    /// <summary>Mostly IQ1_M.</summary>
    IQ1M = 31,

    /// <summary>Mostly bfloat16.</summary>
    BF16 = 32,

    /// <summary>A withdrawn repacked Q4_0 file type; it has no name.</summary>
    Q4_0_4_4 = 33,

    /// <summary>A withdrawn repacked Q4_0 file type; it has no name.</summary>
    Q4_0_4_8 = 34,

    /// <summary>A withdrawn repacked Q4_0 file type; it has no name.</summary>
    Q4_0_8_8 = 35,

    /// <summary>Mostly TQ1_0.</summary>
    TQ1_0 = 36,

    /// <summary>Mostly TQ2_0.</summary>
    TQ2_0 = 37,

    /// <summary>A mixture-of-experts model whose expert tensors are MXFP4.</summary>
    MXFP4MOE = 38,

    /// <summary>Mostly NVFP4.</summary>
    NVFP4 = 39,

    /// <summary>Mostly Q1_0.</summary>
    Q1_0 = 40,

    /// <summary>The file type is absent or not one this reader knows.</summary>
    Unknown = 1024
}
