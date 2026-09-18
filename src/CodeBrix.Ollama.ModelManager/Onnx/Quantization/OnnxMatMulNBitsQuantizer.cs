// -------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for
// license information.
// --------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/python/tools/quantization/matmul_nbits_quantizer.py@v1.30.0

/// <summary>
/// The managed form of ONNX Runtime's <c>MatMulNBitsQuantizer</c> driving <c>DefaultWeightOnlyQuantizer</c> in its
/// QOperator shape: every MatMul with a constant two-dimensional float or half-precision B becomes a
/// <c>MatMulNBits</c> node in the <c>com.microsoft</c> domain, with the packed weight, the per-block scales and, when
/// asymmetric, the packed zero points as new initializers.
/// </summary>
/// <remarks>
/// Outside this shape the managed engine stops rather than guessing: the QDQ form, the <c>GatherBlockQuantized</c>
/// branch, sub-graphs, bfloat16 weights and weights of rank other than two all raise
/// <see cref="NotSupportedException"/>, and the Python engine covers them.
/// </remarks>
internal static class OnnxMatMulNBitsQuantizer
{
    /// <summary>The operator the managed weight-only path emits.</summary>
    internal const string MatMulNBitsOpType = "MatMulNBits";

    /// <summary>
    /// Quantizes a model's weights in place, then drops whatever the replaced nodes stopped asking for.
    /// </summary>
    /// <param name="model">The model to quantize, with any side-file tensors already read in.</param>
    /// <param name="options">The quantization settings.</param>
    internal static void Process(OnnxModel model, OnnxWeightOnlyQuantizationOptions options)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.Bits != 2 && options.Bits != 4 && options.Bits != 8)
        {
            throw new NotSupportedException(
                $"Weight-only quantization supports 2, 4 and 8 bits; {options.Bits} was asked for.");
        }

        if (options.BlockSize <= 0)
        {
            throw new NotSupportedException(
                $"A block must hold at least one value; {options.BlockSize} was asked for.");
        }

        foreach (string opType in options.OpTypesToQuantize)
        {
            if (!string.Equals(opType, "MatMul", StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"The managed weight-only engine quantizes MatMul only; '{opType}' was asked for. "
                    + "Gather and the QDQ form stay on the Python engine.");
            }
        }

        OnnxGraphProto graph = model.Graph;
        OnnxGraphEditor.SetOpsetImport(model.Proto, OnnxQuantizationUtilities.MicrosoftDomain, 1);

        List<OnnxNodeProto> newNodes = new List<OnnxNodeProto>();
        foreach (OnnxNodeProto node in graph.Nodes)
        {
            if (node.HasSubGraphs())
            {
                throw new NotSupportedException(
                    $"The node '{node.Name}' carries a sub-graph, which the managed weight-only engine does not walk. "
                    + "Use the Python engine for models with sub-graphs.");
            }

            bool excluded = node.Name != null && options.NodesToExclude.Contains(node.Name);
            bool included = node.Name != null && options.NodesToInclude.Contains(node.Name);
            if (excluded || (!included && !options.OpTypesToQuantize.Contains(node.OpType ?? string.Empty)))
            {
                newNodes.Add(node);
                continue;
            }

            newNodes.AddRange(QuantizeNode(node, graph, options));
        }

        graph.Nodes.Clear();
        graph.Nodes.AddRange(newNodes);
        OnnxGraphEditor.CleanInitializers(graph);

        //Upstream sorts the nodes as it SAVES the quantized model, not as it quantizes it, and the sort is not the
        //identity on a graph with branches: it walks the graph from the initializers and inputs in name order, so two
        //nodes that could run in either order come out in the order that walk reaches them. The managed engine always
        //writes what it processed, so the sort belongs at the end of the processing.
        OnnxGraphEditor.TopologicalSort(graph);
    }

    private static List<OnnxNodeProto> QuantizeNode(
        OnnxNodeProto node,
        OnnxGraphProto graph,
        OnnxWeightOnlyQuantizationOptions options)
    {
        List<OnnxNodeProto> unchanged = new List<OnnxNodeProto> { node };
        if (!string.Equals(node.OpType, "MatMul", StringComparison.Ordinal))
        {
            if (string.Equals(node.OpType, "Gather", StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "The managed weight-only engine does not emit GatherBlockQuantized; use the Python engine.");
            }

            return unchanged;
        }

        if (node.Inputs.Count < 2)
        {
            return unchanged;
        }

        OnnxTensorProto weight = graph.FindInitializer(node.Inputs[1]);
        if (weight == null)
        {
            // MatMul does not have a constant weight, so upstream skips it.
            return unchanged;
        }

        if (weight.HasExternalData)
        {
            throw new NotSupportedException(
                $"The weight '{weight.Name}' is still in a side file; read the model's external data first.");
        }

        OnnxTensorDataType weightType = (OnnxTensorDataType)(weight.DataType ?? 0);
        if (weightType == OnnxTensorDataType.BFloat16)
        {
            throw new NotSupportedException(
                $"The weight '{weight.Name}' is bfloat16, which the managed weight-only engine does not quantize.");
        }

        if (weightType != OnnxTensorDataType.Float && weightType != OnnxTensorDataType.Float16)
        {
            return unchanged;
        }

        if (weight.Dimensions.Count < 2)
        {
            // The weight has fewer than two dimensions, so upstream skips it.
            return unchanged;
        }

        if (weight.Dimensions.Count != 2)
        {
            throw new NotSupportedException(
                $"The weight '{weight.Name}' has rank {weight.Dimensions.Count}; the managed weight-only engine "
                + "quantizes rank-two weights only. Use the Python engine for batched weights.");
        }

        int rows = checked((int)weight.Dimensions[0]);
        int columns = checked((int)weight.Dimensions[1]);
        float[] values = OnnxQuantizationUtilities.ReadFloatElements(weight, weight.RawData ?? Array.Empty<byte>());
        OnnxQuantizationUtilities.Require(
            values.Length == (long)rows * columns,
            $"The weight '{weight.Name}' carries {values.Length} values but its shape asks for {(long)rows * columns}.");

        int bits = options.Bits;
        int packSize = OnnxBlockwiseQuantizer.PackSize(bits);
        int blockSize = options.BlockSize;
        int blockCount = OnnxBlockwiseQuantizer.MetaRows(rows, blockSize);
        int blobSize = (blockSize + packSize - 1) / packSize;
        int quantizedRows = OnnxBlockwiseQuantizer.QuantizedRows(rows, blockSize, bits);

        byte[] packed = new byte[(long)columns * blockCount * blobSize];
        float[] scales = new float[(long)columns * blockCount];
        byte[] zeroPoints = new byte[(long)columns * ((blockCount + packSize - 1) / packSize)];
        bool halfPrecision = weightType == OnnxTensorDataType.Float16;

        OnnxBlockwiseQuantizer.QuantizeAndTranspose(
            packed,
            scales,
            options.IsSymmetric ? Span<byte>.Empty : zeroPoints,
            values,
            blockSize,
            rows,
            columns,
            columns,
            halfPrecision,
            bits);
        OnnxQuantizationUtilities.Require(
            quantizedRows == blockCount * blobSize,
            "The packed weight layout does not match the block layout.");

        string quantizedName = weight.Name + "_Q" + bits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        OnnxTensorProto quantizedWeight = OnnxQuantizationUtilities.MakeRawTensor(
            quantizedName,
            OnnxTensorDataType.UInt8,
            new long[] { columns, blockCount, blobSize },
            packed);
        OnnxTensorProto scaleTensor = OnnxQuantizationUtilities.MakeRawTensor(
            weight.Name + "_scales",
            weightType,
            new long[] { columns, blockCount },
            OnnxQuantizationUtilities.FloatsToRawBytes(scales, weightType));

        OnnxValueInfoProto declared = graph.FindInput(node.Inputs[1]);
        if (declared != null)
        {
            graph.Inputs.Remove(declared);
        }

        graph.Initializers.Add(quantizedWeight);
        graph.Initializers.Add(scaleTensor);

        OnnxNodeProto quantizedNode = new OnnxNodeProto
        {
            OpType = MatMulNBitsOpType,
            Name = string.IsNullOrEmpty(node.Name) ? string.Empty : node.Name + "_Q" + bits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Domain = OnnxQuantizationUtilities.MicrosoftDomain,
        };
        quantizedNode.Inputs.Add(node.Inputs[0]);
        quantizedNode.Inputs.Add(quantizedName);
        quantizedNode.Inputs.Add(scaleTensor.Name);
        if (!options.IsSymmetric)
        {
            OnnxTensorProto zeroPointTensor = OnnxQuantizationUtilities.MakeRawTensor(
                weight.Name + "_zero_points",
                OnnxTensorDataType.UInt8,
                new long[] { columns, (blockCount + packSize - 1) / packSize },
                zeroPoints);
            graph.Initializers.Add(zeroPointTensor);
            quantizedNode.Inputs.Add(zeroPointTensor.Name);
        }

        quantizedNode.Outputs.Add(node.Outputs[0]);

        // Upstream builds the node through onnx.helper.make_node, which sorts the attribute names.
        SortedDictionary<string, long> attributes =
            new SortedDictionary<string, long>(StringComparer.Ordinal)
            {
                { "K", rows },
                { "N", columns },
                { "bits", bits },
                { "block_size", blockSize },
            };
        if (options.AccuracyLevel.HasValue && options.AccuracyLevel.Value != 0)
        {
            attributes.Add("accuracy_level", options.AccuracyLevel.Value);
        }

        foreach (KeyValuePair<string, long> attribute in attributes)
        {
            quantizedNode.Attributes.Add(OnnxAttributeProto.CreateInt(attribute.Key, attribute.Value));
        }

        return new List<OnnxNodeProto> { quantizedNode };
    }
}
