namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// One weight-only mode the fixture generator produced: the suffix its output files carry and the settings that
/// produced them.
/// </summary>
/// <param name="Suffix">The file-name suffix, such as <c>nbits_b4_bs32_asym</c>.</param>
/// <param name="Bits">The number of bits each value keeps.</param>
/// <param name="BlockSize">The number of values that share one scale.</param>
/// <param name="IsSymmetric">Whether quantization was symmetric.</param>
/// <param name="AccuracyLevel">The accuracy level written on the node, or <see langword="null"/> for none.</param>
internal sealed record OnnxWeightOnlyFixtureMode(
    string Suffix,
    int Bits,
    int BlockSize,
    bool IsSymmetric,
    int? AccuracyLevel);
