using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Every operator the engine implements, by domain and name.
/// </summary>
/// <remarks>
/// <para>
/// These are the standard <c>ai.onnx</c> operators that the decoder graphs this library was built for use,
/// plus a few that cost nothing and turn up constantly, plus the CONTRIBUTED operators that the model builders
/// and the quantizers emit: the block-quantized matrix multiply, grouped-query attention and the two
/// root-mean-square normalizations.
/// </para>
/// <para>
/// An operator that is NOT here is refused when the model is loaded, with the node, the operator and - when it
/// is not a standard one - the domain named. A graph may DECLARE a domain it never uses, and that is accepted:
/// what is refused is a node, not a declaration.
/// </para>
/// </remarks>
internal static class OnnxKernels
{
    /// <summary>The oldest standard operator set the engine will run.</summary>
    /// <remarks>
    /// Three operators changed shape at opset 13 in ways that cannot be told apart from the node alone -
    /// <c>Unsqueeze</c> moved its axes from an attribute to an input, <c>ReduceSum</c> did the same, and
    /// <c>Softmax</c> stopped flattening the tensor at its axis - so a graph written against an older set is
    /// refused rather than run under the newer meaning.
    /// </remarks>
    internal const long MinimumOpset = 13;

    /// <summary>The newest standard operator set the engine has been checked against.</summary>
    internal const long MaximumOpset = 23;

    /// <summary>The domain the contributed operators live in.</summary>
    internal const string ContributedDomain = "com.microsoft";

    private static readonly Dictionary<string, OnnxKernel> Registry = Build();

    /// <summary>The operators the engine implements, sorted, a contributed one qualified by its domain.</summary>
    internal static IReadOnlyList<string> Supported { get; } = SortedNames();

    /// <summary>Finds the kernel for an operator of a domain.</summary>
    /// <param name="domain">The node's domain; empty and <c>ai.onnx</c> both mean the standard set.</param>
    /// <param name="opType">The operator type.</param>
    /// <returns>The kernel, or <see langword="null"/> when the engine does not implement it.</returns>
    internal static OnnxKernel Find(string domain, string opType) =>
        opType != null && Registry.TryGetValue(Key(NormalizeDomain(domain), opType), out OnnxKernel kernel)
            ? kernel
            : null;

    /// <summary>Spells a node's domain the way the registry spells it.</summary>
    /// <param name="domain">The domain as the file has it.</param>
    /// <returns>An empty string for the standard set, otherwise the domain unchanged.</returns>
    internal static string NormalizeDomain(string domain) =>
        string.IsNullOrEmpty(domain) || string.Equals(domain, "ai.onnx", StringComparison.Ordinal)
            ? string.Empty
            : domain;

    private static Dictionary<string, OnnxKernel> Build()
    {
        OnnxKernel[] kernels =
        {
            new OnnxAddKernel(),
            new OnnxCastKernel(),
            new OnnxConcatKernel(),
            new OnnxConstantKernel(),
            new OnnxConstantOfShapeKernel(),
            new OnnxCosKernel(),
            new OnnxDivKernel(),
            new OnnxDynamicQuantizeLinearKernel(),
            new OnnxEqualKernel(),
            new OnnxExpandKernel(),
            new OnnxGatherKernel(),
            new OnnxGreaterKernel(),
            new OnnxGroupQueryAttentionKernel(),
            new OnnxIdentityKernel(),
            new OnnxMatMulIntegerKernel(),
            new OnnxMatMulKernel(),
            new OnnxMatMulNBitsKernel(),
            new OnnxMulKernel(),
            new OnnxNegKernel(),
            new OnnxPowKernel(),
            new OnnxRangeKernel(),
            new OnnxReduceMeanKernel(),
            new OnnxReduceSumKernel(),
            new OnnxReshapeKernel(),
            new OnnxShapeKernel(),
            new OnnxSigmoidKernel(),
            new OnnxEluKernel(),
            new OnnxErfKernel(),
            new OnnxTanhKernel(),
            new OnnxLayerNormalizationKernel(),
            new OnnxSimplifiedLayerNormalizationKernel(),
            new OnnxSinKernel(),
            new OnnxSkipSimplifiedLayerNormalizationKernel(),
            new OnnxSliceKernel(),
            new OnnxSoftmaxKernel(),
            new OnnxSqrtKernel(),
            new OnnxSubKernel(),
            new OnnxTransposeKernel(),
            new OnnxTriluKernel(),
            new OnnxUnsqueezeKernel(),
            new OnnxWhereKernel(),
        };

        Dictionary<string, OnnxKernel> registry =
            new Dictionary<string, OnnxKernel>(kernels.Length, StringComparer.Ordinal);
        foreach (OnnxKernel kernel in kernels)
        {
            registry.Add(Key(NormalizeDomain(kernel.Domain), kernel.OpType), kernel);
        }

        return registry;
    }

    private static string Key(string domain, string opType) =>
        domain.Length == 0 ? opType : domain + "." + opType;

    private static string[] SortedNames()
    {
        string[] names = new string[Registry.Count];
        Registry.Keys.CopyTo(names, 0);
        Array.Sort(names, StringComparer.Ordinal);
        return names;
    }
}
