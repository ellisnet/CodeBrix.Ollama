using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Finds the checked-in ONNX oracle fixtures beside the test assembly and names the pairs the comparison tests walk.
/// </summary>
internal static class OnnxFixtureFiles
{
    /// <summary>The folder the fixtures are copied to.</summary>
    internal static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Onnx", "Fixtures");

    /// <summary>The full path of one fixture.</summary>
    /// <param name="fileName">The fixture's file name.</param>
    /// <returns>The path.</returns>
    internal static string FullPath(string fileName) => Path.Combine(Directory, fileName);

    /// <summary>Every fixture file, model files and side files alike.</summary>
    /// <returns>The file names, ordered.</returns>
    internal static IReadOnlyList<string> All() =>
        System.IO.Directory.GetFiles(Directory)
            .Select(System.IO.Path.GetFileName)
            .Where(name => name.EndsWith(".onnx", StringComparison.Ordinal)
                || name.EndsWith(".onnx.data", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Every fixture model file.</summary>
    /// <returns>The file names, ordered.</returns>
    internal static IReadOnlyList<string> AllModels() =>
        All().Where(name => name.EndsWith(".onnx", StringComparison.Ordinal)).ToList();

    /// <summary>The input models the weight-only comparison walks, with the mode suffixes each was quantized with.</summary>
    /// <returns>One entry per input model.</returns>
    internal static IReadOnlyList<string> WeightOnlyInputs() => new[]
    {
        "matmul_k64_n8_fp32",
        "matmul_k70_n5_fp32",
        "matmul_k33_n3_fp32",
        "matmul_k64_n4_fp16",
        "matmul_k130_n3_fp32",
        "two_matmuls_fp32",
        "gemm_bias_transb_fp32",
        "unsupported_op_fp32",
        "non_constant_b_fp32",
    };

    /// <summary>The input models the dynamic comparison walks. Each has an <c>.inferred.onnx</c> companion.</summary>
    /// <returns>One entry per input model.</returns>
    internal static IReadOnlyList<string> DynamicInputs() => new[]
    {
        "matmul_k64_n8_fp32",
        "matmul_k70_n5_fp32",
        "two_matmuls_fp32",
        "gemm_bias_transb_fp32",
        "gemm_plain_fp32",
        "unsupported_op_fp32",
        "non_constant_b_fp32",
    };

    /// <summary>The weight-only modes a fixture may carry, named as the generator names them.</summary>
    /// <returns>One entry per mode.</returns>
    internal static IReadOnlyList<OnnxWeightOnlyFixtureMode> WeightOnlyModes() => new[]
    {
        new OnnxWeightOnlyFixtureMode("nbits_b4_bs32_asym", 4, 32, false, null),
        new OnnxWeightOnlyFixtureMode("nbits_b4_bs32_sym", 4, 32, true, null),
        new OnnxWeightOnlyFixtureMode("nbits_b4_bs128_asym", 4, 128, false, null),
        new OnnxWeightOnlyFixtureMode("nbits_b4_bs128_sym", 4, 128, true, null),
        new OnnxWeightOnlyFixtureMode("nbits_b8_bs32_asym", 8, 32, false, null),
        new OnnxWeightOnlyFixtureMode("nbits_b8_bs32_sym", 8, 32, true, null),
        new OnnxWeightOnlyFixtureMode("nbits_b8_bs128_asym", 8, 128, false, null),
        new OnnxWeightOnlyFixtureMode("nbits_b8_bs128_sym", 8, 128, true, null),
        new OnnxWeightOnlyFixtureMode("nbits_b4_bs32_asym_acc4", 4, 32, false, 4),
        new OnnxWeightOnlyFixtureMode("nbits_b4_bs32_sym_acc4", 4, 32, true, 4),
    };

    /// <summary>The dynamic modes a fixture may carry, named as the generator names them.</summary>
    /// <returns>One entry per mode.</returns>
    internal static IReadOnlyList<OnnxDynamicFixtureMode> DynamicModes() => new[]
    {
        new OnnxDynamicFixtureMode("dyn_i8", OnnxTensorDataType.Int8, false, false),
        new OnnxDynamicFixtureMode("dyn_u8", OnnxTensorDataType.UInt8, false, false),
        new OnnxDynamicFixtureMode("dyn_i8_pc", OnnxTensorDataType.Int8, true, false),
        new OnnxDynamicFixtureMode("dyn_u8_pc", OnnxTensorDataType.UInt8, true, false),
        new OnnxDynamicFixtureMode("dyn_i8_rr", OnnxTensorDataType.Int8, false, true),
        new OnnxDynamicFixtureMode("dyn_u8_pc_rr", OnnxTensorDataType.UInt8, true, true),
    };

    /// <summary>Whether a fixture file exists.</summary>
    /// <param name="fileName">The fixture's file name.</param>
    /// <returns><see langword="true"/> when the file is there.</returns>
    internal static bool Exists(string fileName) => File.Exists(FullPath(fileName));
}
