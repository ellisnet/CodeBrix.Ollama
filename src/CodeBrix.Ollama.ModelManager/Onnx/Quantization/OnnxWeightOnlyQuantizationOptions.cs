// -------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for
// license information.
// --------------------------------------------------------------------------

using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/python/tools/quantization/matmul_nbits_quantizer.py@v1.30.0

/// <summary>
/// The settings of ONNX Runtime's <c>DefaultWeightOnlyQuantConfig</c>, with the same defaults: 128 values to a block,
/// asymmetric, four bits, no accuracy level and MatMul as the only operator touched. The QDQ form and the
/// <c>GatherBlockQuantized</c> branch are not part of the managed engine.
/// </summary>
internal sealed class OnnxWeightOnlyQuantizationOptions
{
    /// <summary>The number of weight values that share one scale and zero point. The upstream default is 128.</summary>
    internal int BlockSize { get; set; } = 128;

    /// <summary>Whether to quantize symmetrically. The upstream default is asymmetric.</summary>
    internal bool IsSymmetric { get; set; }

    /// <summary>The number of bits each value keeps, 2, 4 or 8. The upstream default is four.</summary>
    internal int Bits { get; set; } = 4;

    /// <summary>
    /// The accuracy level written on the emitted node, or <see langword="null"/> to leave the attribute off. Upstream
    /// leaves it off for both <see langword="null"/> and zero, because most execution providers do not read it.
    /// </summary>
    internal int? AccuracyLevel { get; set; }

    /// <summary>The operator types to quantize. The upstream default is MatMul alone.</summary>
    internal HashSet<string> OpTypesToQuantize { get; } =
        new HashSet<string>(System.StringComparer.Ordinal) { "MatMul" };

    /// <summary>The names of nodes to leave alone whatever else says.</summary>
    internal HashSet<string> NodesToExclude { get; } = new HashSet<string>(System.StringComparer.Ordinal);

    /// <summary>The names of nodes to quantize even when their operator type is not in the set above.</summary>
    internal HashSet<string> NodesToInclude { get; } = new HashSet<string>(System.StringComparer.Ordinal);
}
