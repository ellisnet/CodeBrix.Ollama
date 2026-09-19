using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// One dynamic mode the fixture generator produced: the suffix its output files carry and the settings that produced
/// them.
/// </summary>
/// <param name="Suffix">The file-name suffix, such as <c>dyn_i8</c>.</param>
/// <param name="WeightType">The element type the weights became.</param>
/// <param name="PerChannel">Whether each output channel got its own scale.</param>
/// <param name="ReduceRange">Whether weights were quantized into seven bits.</param>
internal sealed record OnnxDynamicFixtureMode(
    string Suffix,
    OnnxTensorDataType WeightType,
    bool PerChannel,
    bool ReduceRange);
