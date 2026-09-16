// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/file_type.go at commit a43fad18.
namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The display names of the <see cref="GgufFileType"/> values.
/// </summary>
public static class GgufFileTypes
{
    /// <summary>Returns the quantization name of a file type, or <c>"unknown"</c> when it has none.</summary>
    /// <param name="fileType">The file type.</param>
    /// <returns>The name as llama.cpp prints it, for example <c>"Q4_K_M"</c>.</returns>
    public static string GetName(GgufFileType fileType)
    {
        switch (fileType)
        {
            case GgufFileType.F32: return "F32";
            case GgufFileType.F16: return "F16";
            case GgufFileType.Q4_0: return "Q4_0";
            case GgufFileType.Q4_1: return "Q4_1";
            case GgufFileType.Q8_0: return "Q8_0";
            case GgufFileType.Q5_0: return "Q5_0";
            case GgufFileType.Q5_1: return "Q5_1";
            case GgufFileType.Q2K: return "Q2_K";
            case GgufFileType.Q3KS: return "Q3_K_S";
            case GgufFileType.Q3KM: return "Q3_K_M";
            case GgufFileType.Q3KL: return "Q3_K_L";
            case GgufFileType.Q4_K_S: return "Q4_K_S";
            case GgufFileType.Q4_K_M: return "Q4_K_M";
            case GgufFileType.Q5KS: return "Q5_K_S";
            case GgufFileType.Q5KM: return "Q5_K_M";
            case GgufFileType.Q6K: return "Q6_K";
            case GgufFileType.IQ2XXS: return "IQ2_XXS";
            case GgufFileType.IQ2XS: return "IQ2_XS";
            case GgufFileType.Q2KS: return "Q2_K_S";
            case GgufFileType.IQ3XS: return "IQ3_XS";
            case GgufFileType.IQ3XXS: return "IQ3_XXS";
            case GgufFileType.IQ1S: return "IQ1_S";
            case GgufFileType.IQ4NL: return "IQ4_NL";
            case GgufFileType.IQ3S: return "IQ3_S";
            case GgufFileType.IQ3M: return "IQ3_M";
            case GgufFileType.IQ2S: return "IQ2_S";
            case GgufFileType.IQ2M: return "IQ2_M";
            case GgufFileType.IQ4XS: return "IQ4_XS";
            case GgufFileType.IQ1M: return "IQ1_M";
            case GgufFileType.BF16: return "BF16";
            case GgufFileType.TQ1_0: return "TQ1_0";
            case GgufFileType.TQ2_0: return "TQ2_0";
            case GgufFileType.MXFP4MOE: return "MXFP4_MOE";
            case GgufFileType.NVFP4: return "NVFP4";
            case GgufFileType.Q1_0: return "Q1_0";
            default: return "unknown";
        }
    }
}
