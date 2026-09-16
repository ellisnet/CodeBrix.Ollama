namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// A model file's overall quantization. Mirrors <c>enum llama_ftype</c>.
/// The withdrawn values are left out, exactly as the header leaves them out.
/// </summary>
internal enum LlamaFtype : int
{
    /// <summary>Every tensor is 32-bit float.</summary>
    AllF32 = 0,

    /// <summary>Mostly 16-bit float.</summary>
    MostlyF16 = 1,

    /// <summary>Mostly Q4_0.</summary>
    MostlyQ4_0 = 2,

    /// <summary>Mostly Q4_1.</summary>
    MostlyQ4_1 = 3,

    /// <summary>Mostly Q8_0.</summary>
    MostlyQ8_0 = 7,

    /// <summary>Mostly Q5_0.</summary>
    MostlyQ5_0 = 8,

    /// <summary>Mostly Q5_1.</summary>
    MostlyQ5_1 = 9,

    /// <summary>Mostly Q2_K.</summary>
    MostlyQ2K = 10,

    /// <summary>Mostly Q3_K, small.</summary>
    MostlyQ3KS = 11,

    /// <summary>Mostly Q3_K, medium.</summary>
    MostlyQ3KM = 12,

    /// <summary>Mostly Q3_K, large.</summary>
    MostlyQ3KL = 13,

    /// <summary>Mostly Q4_K, small.</summary>
    MostlyQ4KS = 14,

    /// <summary>Mostly Q4_K, medium.</summary>
    MostlyQ4KM = 15,

    /// <summary>Mostly Q5_K, small.</summary>
    MostlyQ5KS = 16,

    /// <summary>Mostly Q5_K, medium.</summary>
    MostlyQ5KM = 17,

    /// <summary>Mostly Q6_K.</summary>
    MostlyQ6K = 18,

    /// <summary>Mostly IQ2_XXS.</summary>
    MostlyIq2Xxs = 19,

    /// <summary>Mostly IQ2_XS.</summary>
    MostlyIq2Xs = 20,

    /// <summary>Mostly Q2_K, small.</summary>
    MostlyQ2KS = 21,

    /// <summary>Mostly IQ3_XS.</summary>
    MostlyIq3Xs = 22,

    /// <summary>Mostly IQ3_XXS.</summary>
    MostlyIq3Xxs = 23,

    /// <summary>Mostly IQ1_S.</summary>
    MostlyIq1S = 24,

    /// <summary>Mostly IQ4_NL.</summary>
    MostlyIq4Nl = 25,

    /// <summary>Mostly IQ3_S.</summary>
    MostlyIq3S = 26,

    /// <summary>Mostly IQ3_M.</summary>
    MostlyIq3M = 27,

    /// <summary>Mostly IQ2_S.</summary>
    MostlyIq2S = 28,

    /// <summary>Mostly IQ2_M.</summary>
    MostlyIq2M = 29,

    /// <summary>Mostly IQ4_XS.</summary>
    MostlyIq4Xs = 30,

    /// <summary>Mostly IQ1_M.</summary>
    MostlyIq1M = 31,

    /// <summary>Mostly BF16.</summary>
    MostlyBf16 = 32,

    /// <summary>Mostly TQ1_0.</summary>
    MostlyTq1_0 = 36,

    /// <summary>Mostly TQ2_0.</summary>
    MostlyTq2_0 = 37,

    /// <summary>Mostly MXFP4 for the mixture-of-experts tensors.</summary>
    MostlyMxfp4Moe = 38,

    /// <summary>Mostly NVFP4.</summary>
    MostlyNvfp4 = 39,

    /// <summary>Mostly Q1_0.</summary>
    MostlyQ1_0 = 40,

    /// <summary>Mostly Q2_0.</summary>
    MostlyQ2_0 = 41,

    /// <summary>Not recorded in the file; the engine guessed.</summary>
    Guessed = 1024,
}
