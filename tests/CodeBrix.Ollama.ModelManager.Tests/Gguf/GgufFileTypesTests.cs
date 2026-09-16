using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests; //was previously: ollama/ollama fs/gguf/file_type.go;

/// <summary>Covers the <c>general.file_type</c> table.</summary>
public sealed class GgufFileTypesTests
{
    [Theory]
    [InlineData(GgufFileType.F32, "F32")]
    [InlineData(GgufFileType.F16, "F16")]
    [InlineData(GgufFileType.Q4_0, "Q4_0")]
    [InlineData(GgufFileType.Q4_1, "Q4_1")]
    [InlineData(GgufFileType.Q8_0, "Q8_0")]
    [InlineData(GgufFileType.Q5_0, "Q5_0")]
    [InlineData(GgufFileType.Q5_1, "Q5_1")]
    [InlineData(GgufFileType.Q2K, "Q2_K")]
    [InlineData(GgufFileType.Q3KS, "Q3_K_S")]
    [InlineData(GgufFileType.Q3KM, "Q3_K_M")]
    [InlineData(GgufFileType.Q3KL, "Q3_K_L")]
    [InlineData(GgufFileType.Q4_K_S, "Q4_K_S")]
    [InlineData(GgufFileType.Q4_K_M, "Q4_K_M")]
    [InlineData(GgufFileType.Q5KS, "Q5_K_S")]
    [InlineData(GgufFileType.Q5KM, "Q5_K_M")]
    [InlineData(GgufFileType.Q6K, "Q6_K")]
    [InlineData(GgufFileType.IQ2XXS, "IQ2_XXS")]
    [InlineData(GgufFileType.IQ2XS, "IQ2_XS")]
    [InlineData(GgufFileType.Q2KS, "Q2_K_S")]
    [InlineData(GgufFileType.IQ3XS, "IQ3_XS")]
    [InlineData(GgufFileType.IQ3XXS, "IQ3_XXS")]
    [InlineData(GgufFileType.IQ1S, "IQ1_S")]
    [InlineData(GgufFileType.IQ4NL, "IQ4_NL")]
    [InlineData(GgufFileType.IQ3S, "IQ3_S")]
    [InlineData(GgufFileType.IQ3M, "IQ3_M")]
    [InlineData(GgufFileType.IQ2S, "IQ2_S")]
    [InlineData(GgufFileType.IQ2M, "IQ2_M")]
    [InlineData(GgufFileType.IQ4XS, "IQ4_XS")]
    [InlineData(GgufFileType.IQ1M, "IQ1_M")]
    [InlineData(GgufFileType.BF16, "BF16")]
    [InlineData(GgufFileType.TQ1_0, "TQ1_0")]
    [InlineData(GgufFileType.TQ2_0, "TQ2_0")]
    [InlineData(GgufFileType.MXFP4MOE, "MXFP4_MOE")]
    [InlineData(GgufFileType.NVFP4, "NVFP4")]
    [InlineData(GgufFileType.Q1_0, "Q1_0")]
    public void GetName_returns_quantization_name_for_every_named_type(GgufFileType fileType, string expected)
    {
        GgufFileTypes.GetName(fileType).Should().Be(expected);
    }

    [Theory]
    [InlineData(GgufFileType.Q4_1F16)]
    [InlineData(GgufFileType.Q4_2)]
    [InlineData(GgufFileType.Q4_3)]
    [InlineData(GgufFileType.Q4_0_4_4)]
    [InlineData(GgufFileType.Q4_0_4_8)]
    [InlineData(GgufFileType.Q4_0_8_8)]
    [InlineData(GgufFileType.Unknown)]
    public void GetName_returns_unknown_for_types_without_a_name(GgufFileType fileType)
    {
        GgufFileTypes.GetName(fileType).Should().Be("unknown");
    }

    [Theory]
    [InlineData(GgufFileType.F32, 0u)]
    [InlineData(GgufFileType.F16, 1u)]
    [InlineData(GgufFileType.Q8_0, 7u)]
    [InlineData(GgufFileType.Q4_K_S, 14u)]
    [InlineData(GgufFileType.Q4_K_M, 15u)]
    [InlineData(GgufFileType.BF16, 32u)]
    [InlineData(GgufFileType.MXFP4MOE, 38u)]
    [InlineData(GgufFileType.NVFP4, 39u)]
    [InlineData(GgufFileType.Q1_0, 40u)]
    [InlineData(GgufFileType.Unknown, 1024u)]
    public void GgufFileType_has_llama_cpp_id_for_every_type(GgufFileType fileType, uint expected)
    {
        ((uint)fileType).Should().Be(expected);
    }
}
