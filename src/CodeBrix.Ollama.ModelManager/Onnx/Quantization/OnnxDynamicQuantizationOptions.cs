// -------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for
// license information.
// --------------------------------------------------------------------------

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/python/tools/quantization/quantize.py@v1.30.0

/// <summary>
/// The settings of ONNX Runtime's <c>quantize_dynamic</c> that reach the operators the managed engine covers, with the
/// same defaults: signed 8-bit weights, one scale for the whole weight, and the full quantized range. The activation
/// type is always unsigned 8-bit, because dynamic quantization supports nothing else, and MatMul is quantized only
/// when its B side is constant, which is upstream's default as well.
/// </summary>
internal sealed class OnnxDynamicQuantizationOptions
{
    /// <summary>The element type the weights become. The upstream default is signed 8-bit.</summary>
    internal OnnxTensorDataType WeightType { get; set; } = OnnxTensorDataType.Int8;

    /// <summary>Whether each output channel of a weight gets its own scale. The upstream default is one for all.</summary>
    internal bool PerChannel { get; set; }

    /// <summary>Whether to quantize weights into seven bits, which can help on machines without VNNI.</summary>
    internal bool ReduceRange { get; set; }

    /// <summary>
    /// The operator types the run may rewrite. The default is the pair this library asks ONNX Runtime's own tools for,
    /// MatMul and Gemm, and it is what keeps the two engines producing the same file.
    /// </summary>
    /// <remarks>
    /// Upstream's default is every key of its integer registry - Conv, Attention, LSTM, Gather, Transpose and
    /// EmbedLayerNormalization as well - and a model quantized with that wider set is a DIFFERENT model: a transformer's
    /// embedding table is a Gather, and the wider set quantizes it. The managed engine rewrites MatMul (and the Gemm it
    /// first rewrites into one) and carries every other operator through untouched; naming an operator here that it
    /// cannot emit is refused rather than silently ignored.
    /// </remarks>
    internal System.Collections.Generic.HashSet<string> OpTypesToQuantize { get; } =
        new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal) { "MatMul", "Gemm" };

    /// <summary>The names of nodes to leave alone.</summary>
    internal System.Collections.Generic.HashSet<string> NodesToExclude { get; } =
        new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

    /// <summary>The names of the only nodes to quantize, or an empty set to consider every eligible node.</summary>
    internal System.Collections.Generic.HashSet<string> NodesToQuantize { get; } =
        new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
}
