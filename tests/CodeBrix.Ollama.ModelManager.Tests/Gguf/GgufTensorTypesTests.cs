using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests; //was previously: ollama/ollama fs/gguf/tensor.go;

/// <summary>Covers the ggml type table: names, block geometry and per-value sizes.</summary>
public sealed class GgufTensorTypesTests
{
    [Theory]
    [InlineData(GgufTensorType.F32, "f32")]
    [InlineData(GgufTensorType.F16, "f16")]
    [InlineData(GgufTensorType.Q4_0, "q4_0")]
    [InlineData(GgufTensorType.Q4_1, "q4_1")]
    [InlineData(GgufTensorType.Q4_2, "q4_2")]
    [InlineData(GgufTensorType.Q4_3, "q4_3")]
    [InlineData(GgufTensorType.Q5_0, "q5_0")]
    [InlineData(GgufTensorType.Q5_1, "q5_1")]
    [InlineData(GgufTensorType.Q8_0, "q8_0")]
    [InlineData(GgufTensorType.Q8_1, "q8_1")]
    [InlineData(GgufTensorType.Q2_K, "q2_k")]
    [InlineData(GgufTensorType.Q3_K, "q3_k")]
    [InlineData(GgufTensorType.Q4_K, "q4_k")]
    [InlineData(GgufTensorType.Q5_K, "q5_k")]
    [InlineData(GgufTensorType.Q6_K, "q6_k")]
    [InlineData(GgufTensorType.Q8_K, "q8_k")]
    [InlineData(GgufTensorType.IQ2_XXS, "iq2_xxs")]
    [InlineData(GgufTensorType.IQ2_XS, "iq2_xs")]
    [InlineData(GgufTensorType.IQ3_XXS, "iq3_xxs")]
    [InlineData(GgufTensorType.IQ1_S, "iq1_s")]
    [InlineData(GgufTensorType.IQ4_NL, "iq4_nl")]
    [InlineData(GgufTensorType.IQ3_S, "iq3_s")]
    [InlineData(GgufTensorType.IQ2_S, "iq2_s")]
    [InlineData(GgufTensorType.IQ4_XS, "iq4_xs")]
    [InlineData(GgufTensorType.I8, "i8")]
    [InlineData(GgufTensorType.I16, "i16")]
    [InlineData(GgufTensorType.I32, "i32")]
    [InlineData(GgufTensorType.I64, "i64")]
    [InlineData(GgufTensorType.F64, "f64")]
    [InlineData(GgufTensorType.IQ1_M, "iq1_m")]
    [InlineData(GgufTensorType.BF16, "bf16")]
    [InlineData(GgufTensorType.Q4_0_4_4, "q4_0_4_4")]
    [InlineData(GgufTensorType.Q4_0_4_8, "q4_0_4_8")]
    [InlineData(GgufTensorType.Q4_0_8_8, "q4_0_8_8")]
    [InlineData(GgufTensorType.TQ1_0, "tq1_0")]
    [InlineData(GgufTensorType.TQ2_0, "tq2_0")]
    [InlineData(GgufTensorType.IQ4_NL_4_4, "iq4_nl_4_4")]
    [InlineData(GgufTensorType.IQ4_NL_4_8, "iq4_nl_4_8")]
    [InlineData(GgufTensorType.IQ4_NL_8_8, "iq4_nl_8_8")]
    [InlineData(GgufTensorType.MXFP4, "mxfp4")]
    [InlineData(GgufTensorType.NVFP4, "nvfp4")]
    [InlineData(GgufTensorType.Q1_0, "q1_0")]
    public void GetName_returns_ggml_name_for_every_known_type(GgufTensorType tensorType, string expected)
        => GgufTensorTypes.GetName(tensorType).Should().Be(expected);

    [Fact]
    public void GetName_returns_unknown_for_unknown_id()
        => GgufTensorTypes.GetName((GgufTensorType)9999).Should().Be("unknown");

    [Theory]
    [InlineData(GgufTensorType.F32, 0u)]
    [InlineData(GgufTensorType.F16, 1u)]
    [InlineData(GgufTensorType.Q4_0, 2u)]
    [InlineData(GgufTensorType.Q4_1, 3u)]
    [InlineData(GgufTensorType.Q4_2, 4u)]
    [InlineData(GgufTensorType.Q4_3, 5u)]
    [InlineData(GgufTensorType.Q5_0, 6u)]
    [InlineData(GgufTensorType.Q8_0, 8u)]
    [InlineData(GgufTensorType.Q2_K, 10u)]
    [InlineData(GgufTensorType.Q8_K, 15u)]
    [InlineData(GgufTensorType.IQ2_XXS, 16u)]
    [InlineData(GgufTensorType.IQ4_XS, 23u)]
    [InlineData(GgufTensorType.I8, 24u)]
    [InlineData(GgufTensorType.F64, 28u)]
    [InlineData(GgufTensorType.IQ1_M, 29u)]
    [InlineData(GgufTensorType.BF16, 30u)]
    [InlineData(GgufTensorType.Q4_0_4_4, 31u)]
    [InlineData(GgufTensorType.TQ1_0, 34u)]
    [InlineData(GgufTensorType.IQ4_NL_4_4, 36u)]
    [InlineData(GgufTensorType.MXFP4, 39u)]
    [InlineData(GgufTensorType.NVFP4, 40u)]
    [InlineData(GgufTensorType.Q1_0, 41u)]
    public void GgufTensorType_has_ggml_id_for_every_type(GgufTensorType tensorType, uint expected)
        => ((uint)tensorType).Should().Be(expected);

    [Theory]
    [InlineData(GgufTensorType.F32, 1L, 4L)]
    [InlineData(GgufTensorType.F16, 1L, 2L)]
    [InlineData(GgufTensorType.BF16, 1L, 2L)]
    [InlineData(GgufTensorType.I8, 1L, 1L)]
    [InlineData(GgufTensorType.I16, 1L, 2L)]
    [InlineData(GgufTensorType.I32, 1L, 4L)]
    [InlineData(GgufTensorType.I64, 1L, 8L)]
    [InlineData(GgufTensorType.F64, 1L, 8L)]
    [InlineData(GgufTensorType.Q4_0, 32L, 18L)]
    [InlineData(GgufTensorType.Q4_1, 32L, 20L)]
    [InlineData(GgufTensorType.Q5_0, 32L, 22L)]
    [InlineData(GgufTensorType.Q5_1, 32L, 24L)]
    [InlineData(GgufTensorType.Q8_0, 32L, 34L)]
    [InlineData(GgufTensorType.Q8_1, 32L, 36L)]
    [InlineData(GgufTensorType.Q2_K, 256L, 84L)]
    [InlineData(GgufTensorType.Q3_K, 256L, 110L)]
    [InlineData(GgufTensorType.Q4_K, 256L, 144L)]
    [InlineData(GgufTensorType.Q5_K, 256L, 176L)]
    [InlineData(GgufTensorType.Q6_K, 256L, 210L)]
    [InlineData(GgufTensorType.Q8_K, 256L, 292L)]
    [InlineData(GgufTensorType.IQ2_XXS, 256L, 66L)]
    [InlineData(GgufTensorType.IQ2_XS, 256L, 74L)]
    [InlineData(GgufTensorType.IQ3_XXS, 256L, 98L)]
    [InlineData(GgufTensorType.IQ1_S, 256L, 50L)]
    [InlineData(GgufTensorType.IQ4_NL, 32L, 18L)]
    [InlineData(GgufTensorType.IQ3_S, 256L, 110L)]
    [InlineData(GgufTensorType.IQ2_S, 256L, 82L)]
    [InlineData(GgufTensorType.IQ4_XS, 256L, 136L)]
    [InlineData(GgufTensorType.IQ1_M, 256L, 56L)]
    [InlineData(GgufTensorType.MXFP4, 32L, 17L)]
    [InlineData(GgufTensorType.NVFP4, 64L, 36L)]
    [InlineData(GgufTensorType.Q1_0, 128L, 18L)]
    public void GetBlockSizeAndGetTypeSize_match_ggml(GgufTensorType tensorType, long blockSize, long typeSize)
    {
        //Arrange
        //Act
        long actualBlockSize = GgufTensorTypes.GetBlockSize(tensorType);
        long actualTypeSize = GgufTensorTypes.GetTypeSize(tensorType);

        //Assert
        actualBlockSize.Should().Be(blockSize);
        actualTypeSize.Should().Be(typeSize);
    }

    [Theory]
    [InlineData(GgufTensorType.Q4_2)]
    [InlineData(GgufTensorType.Q4_3)]
    [InlineData(GgufTensorType.Q4_0_4_4)]
    [InlineData(GgufTensorType.Q4_0_4_8)]
    [InlineData(GgufTensorType.Q4_0_8_8)]
    [InlineData(GgufTensorType.TQ1_0)]
    [InlineData(GgufTensorType.TQ2_0)]
    [InlineData(GgufTensorType.IQ4_NL_4_4)]
    [InlineData(GgufTensorType.IQ4_NL_4_8)]
    [InlineData(GgufTensorType.IQ4_NL_8_8)]
    public void GetTypeSize_returns_zero_for_types_with_no_defined_size(GgufTensorType tensorType)
        => GgufTensorTypes.GetTypeSize(tensorType).Should().Be(0L);

    [Theory]
    [InlineData(GgufTensorType.F32, 4d)]
    [InlineData(GgufTensorType.F16, 2d)]
    [InlineData(GgufTensorType.Q4_0, 0.5625d)]
    [InlineData(GgufTensorType.Q4_K, 0.5625d)]
    [InlineData(GgufTensorType.Q8_0, 1.0625d)]
    [InlineData(GgufTensorType.MXFP4, 0.53125d)]
    [InlineData(GgufTensorType.NVFP4, 0.5625d)]
    [InlineData(GgufTensorType.Q1_0, 0.140625d)]
    public void GetBytesPerElement_divides_type_size_by_block_size(GgufTensorType tensorType, double expected)
        => GgufTensorTypes.GetBytesPerElement(tensorType).Should().Be(expected);
}
