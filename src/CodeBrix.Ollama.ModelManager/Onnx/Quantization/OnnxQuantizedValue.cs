// -------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for
// license information.
// --------------------------------------------------------------------------

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/python/tools/quantization/quant_utils.py@v1.30.0

/// <summary>
/// What one tensor became once it was quantized: the name that now carries its integers, and the names of the scale
/// and zero point that turn them back into real numbers.
/// </summary>
internal sealed class OnnxQuantizedValue
{
    /// <summary>The name the quantized integers are published under.</summary>
    internal string QuantizedName { get; set; }

    /// <summary>The name of the scale tensor.</summary>
    internal string ScaleName { get; set; }

    /// <summary>The name of the zero-point tensor.</summary>
    internal string ZeroPointName { get; set; }

    /// <summary>The axis the scale varies along, or <see langword="null"/> when one scale covers the tensor.</summary>
    internal int? Axis { get; set; }
}
