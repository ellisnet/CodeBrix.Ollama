namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml.h;


/// <summary>
/// A tensor element type. Mirrors <c>enum ggml_type</c>.
/// The withdrawn values are left out, exactly as the header leaves them out.
/// </summary>
internal enum GgmlType : int
{
    /// <summary>32-bit float.</summary>
    F32 = 0,

    /// <summary>16-bit float.</summary>
    F16 = 1,

    /// <summary>Q4_0.</summary>
    Q4_0 = 2,

    /// <summary>Q4_1.</summary>
    Q4_1 = 3,

    /// <summary>Q5_0.</summary>
    Q5_0 = 6,

    /// <summary>Q5_1.</summary>
    Q5_1 = 7,

    /// <summary>Q8_0.</summary>
    Q8_0 = 8,

    /// <summary>Q8_1.</summary>
    Q8_1 = 9,

    /// <summary>Q2_K.</summary>
    Q2K = 10,

    /// <summary>Q3_K.</summary>
    Q3K = 11,

    /// <summary>Q4_K.</summary>
    Q4K = 12,

    /// <summary>Q5_K.</summary>
    Q5K = 13,

    /// <summary>Q6_K.</summary>
    Q6K = 14,

    /// <summary>Q8_K.</summary>
    Q8K = 15,

    /// <summary>IQ2_XXS.</summary>
    Iq2Xxs = 16,

    /// <summary>IQ2_XS.</summary>
    Iq2Xs = 17,

    /// <summary>IQ3_XXS.</summary>
    Iq3Xxs = 18,

    /// <summary>IQ1_S.</summary>
    Iq1S = 19,

    /// <summary>IQ4_NL.</summary>
    Iq4Nl = 20,

    /// <summary>IQ3_S.</summary>
    Iq3S = 21,

    /// <summary>IQ2_S.</summary>
    Iq2S = 22,

    /// <summary>IQ4_XS.</summary>
    Iq4Xs = 23,

    /// <summary>8-bit signed integer.</summary>
    I8 = 24,

    /// <summary>16-bit signed integer.</summary>
    I16 = 25,

    /// <summary>32-bit signed integer.</summary>
    I32 = 26,

    /// <summary>64-bit signed integer.</summary>
    I64 = 27,

    /// <summary>64-bit float.</summary>
    F64 = 28,

    /// <summary>IQ1_M.</summary>
    Iq1M = 29,

    /// <summary>Brain float 16.</summary>
    Bf16 = 30,

    /// <summary>TQ1_0.</summary>
    Tq1_0 = 34,

    /// <summary>TQ2_0.</summary>
    Tq2_0 = 35,

    /// <summary>MXFP4, one block.</summary>
    Mxfp4 = 39,

    /// <summary>NVFP4, four blocks with an E4M3 scale.</summary>
    Nvfp4 = 40,

    /// <summary>Q1_0.</summary>
    Q1_0 = 41,

    /// <summary>Q2_0.</summary>
    Q2_0 = 42,

    /// <summary>The number of defined types; not a type itself.</summary>
    Count = 43,
}
