namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A file type <see cref="ModelRunner.QuantizeAsync"/> can rewrite a GGUF model as. The numbers are the
/// inference engine's own file-type ids and never change, so a value written into a file by one version is
/// read as the same type by another.
/// </summary>
/// <remarks>
/// <para>
/// The members are exactly the file types the engine will write - the ones its quantizer maps to a tensor
/// type - and every one of them carries the number the engine's own enumeration gives it. The three that
/// are not quantizations at all (<see cref="F32"/>, <see cref="F16"/> and <see cref="BF16"/>) are here
/// because the engine writes them through the same call, which is how a BF16 file is turned into an F16 one
/// without a checkpoint being read again.
/// </para>
/// <para>
/// NAMING. A "K" type is a k-quantization, whose blocks carry a scale of their own; the trailing S, M and L
/// choose how much of the model is written at the next type up, which is why a K_M file is larger and closer
/// to the original than a K_S one. An "IQ" type is an importance quantization: it is the smallest of them
/// all, and the engine quantizes some of its tensors with more bits when no importance matrix is supplied.
/// Only a type's SIZE and its error are chosen here - which tensors get which treatment is the engine's rule,
/// and this library never second-guesses it.
/// </para>
/// <para>
/// <see cref="F32"/> is zero, and therefore what a default-valued variable of this type holds. That is the
/// engine's own numbering, kept rather than corrected so that the number in a file and the number here are
/// one thing; every member of <see cref="ModelRunner.QuantizeAsync"/>'s type parameter is passed explicitly
/// in any case.
/// </para>
/// </remarks>
public enum GgufQuantizationType
{
    /// <summary>Every tensor 32-bit float: no quantization at all, and the largest file of the lot.</summary>
    F32 = 0,

    /// <summary>Mostly 16-bit float.</summary>
    F16 = 1,

    /// <summary>4-bit, one scale per block of 32.</summary>
    Q4_0 = 2,

    /// <summary>4-bit, a scale and a minimum per block of 32.</summary>
    Q4_1 = 3,

    /// <summary>8-bit, one scale per block of 32. The closest quantization to the original file.</summary>
    Q8_0 = 7,

    /// <summary>5-bit, one scale per block of 32.</summary>
    Q5_0 = 8,

    /// <summary>5-bit, a scale and a minimum per block of 32.</summary>
    Q5_1 = 9,

    /// <summary>2-bit k-quantization.</summary>
    Q2_K = 10,

    /// <summary>3-bit k-quantization, small.</summary>
    Q3_K_S = 11,

    /// <summary>3-bit k-quantization, medium.</summary>
    Q3_K_M = 12,

    /// <summary>3-bit k-quantization, large.</summary>
    Q3_K_L = 13,

    /// <summary>4-bit k-quantization, small.</summary>
    Q4_K_S = 14,

    /// <summary>4-bit k-quantization, medium. The usual choice when a model has to be made smaller.</summary>
    Q4_K_M = 15,

    /// <summary>5-bit k-quantization, small.</summary>
    Q5_K_S = 16,

    /// <summary>5-bit k-quantization, medium.</summary>
    Q5_K_M = 17,

    /// <summary>6-bit k-quantization.</summary>
    Q6_K = 18,

    /// <summary>2-bit importance quantization, extra extra small.</summary>
    IQ2_XXS = 19,

    /// <summary>2-bit importance quantization, extra small.</summary>
    IQ2_XS = 20,

    /// <summary>2-bit k-quantization, small.</summary>
    Q2_K_S = 21,

    /// <summary>3-bit importance quantization, extra small.</summary>
    IQ3_XS = 22,

    /// <summary>3-bit importance quantization, extra extra small.</summary>
    IQ3_XXS = 23,

    /// <summary>1-bit importance quantization, small.</summary>
    IQ1_S = 24,

    /// <summary>4-bit importance quantization, non-linear.</summary>
    IQ4_NL = 25,

    /// <summary>3-bit importance quantization, small.</summary>
    IQ3_S = 26,

    /// <summary>3-bit importance quantization, medium.</summary>
    IQ3_M = 27,

    /// <summary>2-bit importance quantization, small.</summary>
    IQ2_S = 28,

    /// <summary>2-bit importance quantization, medium.</summary>
    IQ2_M = 29,

    /// <summary>4-bit importance quantization, extra small.</summary>
    IQ4_XS = 30,

    /// <summary>1-bit importance quantization, medium.</summary>
    IQ1_M = 31,

    /// <summary>Mostly 16-bit brain float: the same size as <see cref="F16"/> with a wider exponent.</summary>
    BF16 = 32,

    /// <summary>Ternary quantization, 1.69 bits per weight.</summary>
    TQ1_0 = 36,

    /// <summary>Ternary quantization, 2.06 bits per weight.</summary>
    TQ2_0 = 37,

    /// <summary>4-bit microscaling float for the mixture-of-experts tensors only.</summary>
    MXFP4_MOE = 38,

    /// <summary>1-bit quantization, 1.125 bits per weight.</summary>
    Q1_0 = 40,

    /// <summary>2-bit quantization in groups of 64, 2.25 bits per weight.</summary>
    Q2_0 = 41,
}
