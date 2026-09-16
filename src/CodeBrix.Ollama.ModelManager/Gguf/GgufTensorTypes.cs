// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/tensor.go at commit a43fad18.
namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The names and block geometry of every <see cref="GgufTensorType"/>.
/// </summary>
public static class GgufTensorTypes
{
    /// <summary>Returns the lowercase ggml name of a tensor type, or <c>"unknown"</c> when the id is not known.</summary>
    /// <param name="tensorType">The tensor type.</param>
    /// <returns>The lowercase name.</returns>
    public static string GetName(GgufTensorType tensorType)
    {
        switch (tensorType)
        {
            case GgufTensorType.F32: return "f32";
            case GgufTensorType.F16: return "f16";
            case GgufTensorType.Q4_0: return "q4_0";
            case GgufTensorType.Q4_1: return "q4_1";
            case GgufTensorType.Q4_2: return "q4_2";
            case GgufTensorType.Q4_3: return "q4_3";
            case GgufTensorType.Q5_0: return "q5_0";
            case GgufTensorType.Q5_1: return "q5_1";
            case GgufTensorType.Q8_0: return "q8_0";
            case GgufTensorType.Q8_1: return "q8_1";
            case GgufTensorType.Q2_K: return "q2_k";
            case GgufTensorType.Q3_K: return "q3_k";
            case GgufTensorType.Q4_K: return "q4_k";
            case GgufTensorType.Q5_K: return "q5_k";
            case GgufTensorType.Q6_K: return "q6_k";
            case GgufTensorType.Q8_K: return "q8_k";
            case GgufTensorType.IQ2_XXS: return "iq2_xxs";
            case GgufTensorType.IQ2_XS: return "iq2_xs";
            case GgufTensorType.IQ3_XXS: return "iq3_xxs";
            case GgufTensorType.IQ1_S: return "iq1_s";
            case GgufTensorType.IQ4_NL: return "iq4_nl";
            case GgufTensorType.IQ3_S: return "iq3_s";
            case GgufTensorType.IQ2_S: return "iq2_s";
            case GgufTensorType.IQ4_XS: return "iq4_xs";
            case GgufTensorType.I8: return "i8";
            case GgufTensorType.I16: return "i16";
            case GgufTensorType.I32: return "i32";
            case GgufTensorType.I64: return "i64";
            case GgufTensorType.F64: return "f64";
            case GgufTensorType.IQ1_M: return "iq1_m";
            case GgufTensorType.BF16: return "bf16";
            case GgufTensorType.Q4_0_4_4: return "q4_0_4_4";
            case GgufTensorType.Q4_0_4_8: return "q4_0_4_8";
            case GgufTensorType.Q4_0_8_8: return "q4_0_8_8";
            case GgufTensorType.TQ1_0: return "tq1_0";
            case GgufTensorType.TQ2_0: return "tq2_0";
            case GgufTensorType.IQ4_NL_4_4: return "iq4_nl_4_4";
            case GgufTensorType.IQ4_NL_4_8: return "iq4_nl_4_8";
            case GgufTensorType.IQ4_NL_8_8: return "iq4_nl_8_8";
            case GgufTensorType.MXFP4: return "mxfp4";
            case GgufTensorType.NVFP4: return "nvfp4";
            case GgufTensorType.Q1_0: return "q1_0";
            default: return "unknown";
        }
    }

    /// <summary>Returns how many values one block of a tensor type holds.</summary>
    /// <param name="tensorType">The tensor type.</param>
    /// <returns>The block size in values. Unknown ids fall into the 256-value k-quantization group.</returns>
    public static long GetBlockSize(GgufTensorType tensorType)
    {
        switch (tensorType)
        {
            case GgufTensorType.F32:
            case GgufTensorType.F16:
            case GgufTensorType.I8:
            case GgufTensorType.I16:
            case GgufTensorType.I32:
            case GgufTensorType.I64:
            case GgufTensorType.F64:
            case GgufTensorType.BF16:
                return 1;
            case GgufTensorType.NVFP4:
                return 64;
            case GgufTensorType.Q1_0:
                return 128;
            case GgufTensorType.Q4_0:
            case GgufTensorType.Q4_1:
            case GgufTensorType.Q5_0:
            case GgufTensorType.Q5_1:
            case GgufTensorType.Q8_0:
            case GgufTensorType.Q8_1:
            case GgufTensorType.IQ4_NL:
            case GgufTensorType.MXFP4:
                return 32;
            default:
                return 256;
        }
    }

    /// <summary>Returns how many bytes one block of a tensor type occupies.</summary>
    /// <param name="tensorType">The tensor type.</param>
    /// <returns>The block size in bytes, or 0 when the type has no defined size.</returns>
    public static long GetTypeSize(GgufTensorType tensorType)
    {
        long blockSize = GetBlockSize(tensorType);
        switch (tensorType)
        {
            case GgufTensorType.F32: return 4;
            case GgufTensorType.F16: return 2;
            case GgufTensorType.Q4_0: return 2 + blockSize / 2;
            case GgufTensorType.Q4_1: return 2 + 2 + blockSize / 2;
            case GgufTensorType.Q5_0: return 2 + 4 + blockSize / 2;
            case GgufTensorType.Q5_1: return 2 + 2 + 4 + blockSize / 2;
            case GgufTensorType.Q8_0: return 2 + blockSize;
            case GgufTensorType.Q8_1: return 2 + 2 + blockSize;
            case GgufTensorType.Q2_K: return blockSize / 16 + blockSize / 4 + 2 + 2;
            case GgufTensorType.Q3_K: return blockSize / 8 + blockSize / 4 + 12 + 2;
            case GgufTensorType.Q4_K: return 2 + 2 + 12 + blockSize / 2;
            case GgufTensorType.Q5_K: return 2 + 2 + 12 + blockSize / 8 + blockSize / 2;
            case GgufTensorType.Q6_K: return blockSize / 2 + blockSize / 4 + blockSize / 16 + 2;
            case GgufTensorType.Q8_K: return 4 + blockSize + 2 * blockSize / 16;
            case GgufTensorType.IQ2_XXS: return 2 + 2 * blockSize / 8;
            case GgufTensorType.IQ2_XS: return 2 + 2 * blockSize / 8 + blockSize / 32;
            case GgufTensorType.IQ3_XXS: return 2 + blockSize / 4 + blockSize / 8;
            case GgufTensorType.IQ1_S: return 2 + blockSize / 8 + blockSize / 16;
            case GgufTensorType.IQ4_NL: return 2 + blockSize / 2;
            case GgufTensorType.IQ3_S: return 2 + blockSize / 4 + blockSize / 8 + blockSize / 32 + 4;
            case GgufTensorType.IQ2_S: return 2 + blockSize / 4 + blockSize / 16;
            case GgufTensorType.IQ4_XS: return 2 + 2 + blockSize / 2 + blockSize / 64;
            case GgufTensorType.I8: return 1;
            case GgufTensorType.I16: return 2;
            case GgufTensorType.I32: return 4;
            case GgufTensorType.I64: return 8;
            case GgufTensorType.F64: return 8;
            case GgufTensorType.IQ1_M: return blockSize / 8 + blockSize / 16 + blockSize / 32;
            case GgufTensorType.BF16: return 2;
            case GgufTensorType.MXFP4: return 1 + blockSize / 2;
            case GgufTensorType.NVFP4: return 4 + blockSize / 2;
            case GgufTensorType.Q1_0: return 2 + blockSize / 8;
            default: return 0;
        }
    }

    /// <summary>Returns the average number of bytes one value of a tensor type occupies.</summary>
    /// <param name="tensorType">The tensor type.</param>
    /// <returns>The block byte size divided by the block value count.</returns>
    public static double GetBytesPerElement(GgufTensorType tensorType)
    {
        return (double)GetTypeSize(tensorType) / GetBlockSize(tensorType);
    }
}
