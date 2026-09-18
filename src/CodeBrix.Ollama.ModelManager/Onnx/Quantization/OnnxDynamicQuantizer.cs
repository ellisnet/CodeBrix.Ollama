// -------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for
// license information.
// --------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/python/tools/quantization/onnx_quantizer.py@v1.30.0

/// <summary>
/// The managed form of ONNX Runtime's <c>quantize_dynamic</c> for the two operators decision D14 covers. A Gemm whose
/// alpha, beta and transA leave it a plain matrix product first becomes a MatMul and an Add, exactly as upstream does,
/// and then every MatMul with a constant float or half-precision B becomes
/// <c>DynamicQuantizeLinear -> MatMulInteger -> Cast -> Mul</c> with a second Mul joining the two scales.
/// </summary>
/// <remarks>
/// The managed engine runs no shape inference of its own, so it requires a model that has already been through it -
/// which is what <c>quant_pre_process</c> and <c>quantize_dynamic</c> do on the Python side, and what the
/// <c>onnx.infer</c> metadata entry records. Every operator other than the two it rewrites is carried through
/// untouched, which is what ONNX Runtime does when it is asked for those two operator types and only those; an
/// operator its integer registry would also rewrite is refused only when this run asks for it by name. It stops
/// rather than guess when a node carries a sub-graph, or when a Gemm's B side is not an initializer.
///
/// The logic is drawn from <c>onnx_quantizer.py</c> (the quantizer itself), <c>operators/matmul.py</c> (the
/// MatMulInteger shape it emits), <c>operators/gemm.py</c> and <c>registry.py</c> (which say that dynamic
/// quantization reaches Gemm only through the MatMul rewrite), <c>base_quantizer.py</c> (the weight quantization)
/// and <c>quantize.py</c> (the defaults), all at the same tag.
/// </remarks>
internal static class OnnxDynamicQuantizer
{
    private static readonly string[] OtherIntegerRegistryOpTypes =
    {
        "Conv",
        "Attention",
        "LSTM",
        "Gather",
        "Transpose",
        "EmbedLayerNormalization",
    };

    /// <summary>Quantizes a model's MatMul and Gemm nodes in place.</summary>
    /// <param name="model">The model to quantize, with any side-file tensors already read in.</param>
    /// <param name="options">The quantization settings.</param>
    internal static void Process(OnnxModel model, OnnxDynamicQuantizationOptions options)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.WeightType != OnnxTensorDataType.Int8 && options.WeightType != OnnxTensorDataType.UInt8)
        {
            throw new NotSupportedException(
                $"Dynamic quantization supports 8-bit weights only; {options.WeightType} was asked for.");
        }

        if (!string.Equals(
                model.Proto.GetMetadataValue(OnnxQuantizationUtilities.InferMetadataKey),
                OnnxQuantizationUtilities.InferMetadataValue,
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "The managed dynamic engine runs no shape inference, so it needs a model that already carries the "
                + "'onnx.infer' metadata entry. Run the model through the Python engine's preprocessing first.");
        }

        OnnxGraphProto graph = model.Graph;
        long opsetVersion = OnnxGraphEditor.GetOpsetVersion(model.Proto);
        if (opsetVersion <= 10)
        {
            throw new NotSupportedException(
                $"Dynamic quantization through DynamicQuantizeLinear needs opset 11 or later; the model imports {opsetVersion}.");
        }

        foreach (OnnxNodeProto node in graph.Nodes)
        {
            if (node.HasSubGraphs())
            {
                throw new NotSupportedException(
                    $"The node '{node.Name}' carries a sub-graph, which the managed dynamic engine does not walk. "
                    + "Use the Python engine for models with sub-graphs.");
            }

            foreach (string opType in OtherIntegerRegistryOpTypes)
            {
                //Only when this run ASKS for the operator: upstream rewrites it when its default set of operator types
                //is in force, and this library never asks for that set. A Gather or a Transpose nobody asked about is
                //carried through, which is what ONNX Runtime does with the pair of operator types this library names -
                //and every real transformer holds both.
                if (string.Equals(node.OpType, opType, StringComparison.Ordinal)
                    && options.OpTypesToQuantize.Contains(opType))
                {
                    throw new NotSupportedException(
                        $"ONNX Runtime's dynamic quantization rewrites '{opType}' as well, which the managed engine "
                        + "does not port. Leave it out of the operator types to quantize, or use the Python engine "
                        + "for this model.");
                }
            }
        }

        RejectNonConstantGemmWeights(graph);
        OnnxGraphEditor.ReplaceGemmWithMatMul(graph);
        MarkInitializersThatHaveBeenThroughASideFile(graph);
        Dictionary<string, int> valueTypes = BuildValueTypes(graph);
        AddInferredValueInfos(graph, valueTypes);

        HashSet<string> tensorNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxValueInfoProto output in graph.Outputs)
        {
            tensorNames.Add(output.Name ?? string.Empty);
        }

        foreach (OnnxValueInfoProto input in graph.Inputs)
        {
            tensorNames.Add(input.Name ?? string.Empty);
        }

        foreach (OnnxNodeProto node in graph.Nodes)
        {
            foreach (string output in node.Outputs)
            {
                tensorNames.Add(output);
            }
        }

        HashSet<string> generated = OnnxGraphEditor.GetNonInitializerInputs(graph);
        Dictionary<string, OnnxQuantizedValue> quantized =
            new Dictionary<string, OnnxQuantizedValue>(StringComparer.Ordinal);
        List<OnnxNodeProto> newNodes = new List<OnnxNodeProto>();
        List<OnnxNodeProto> original = new List<OnnxNodeProto>(graph.Nodes);

        foreach (OnnxNodeProto node in original)
        {
            int before = newNodes.Count;
            if (ShouldQuantizeMatMul(node, graph, valueTypes, options))
            {
                if (graph.FindInitializer(node.Inputs[0]) == null && !tensorNames.Contains(node.Inputs[0]))
                {
                    throw new System.IO.InvalidDataException(
                        $"The MatMul node '{node.Name}' reads '{node.Inputs[0]}', which nothing in the graph provides.");
                }

                QuantizeMatMul(node, graph, valueTypes, quantized, newNodes, options);
            }
            else
            {
                DequantizeInputs(node, graph, quantized, generated, newNodes);
                newNodes.Add(node);
            }

            for (int index = before; index < newNodes.Count; index++)
            {
                foreach (string output in newNodes[index].Outputs)
                {
                    generated.Add(output);
                }
            }
        }

        foreach (OnnxValueInfoProto output in graph.Outputs)
        {
            OnnxNodeProto dequantize = BuildDequantizeNode(output.Name, quantized, generated, graph, newNodes);
            if (dequantize != null)
            {
                newNodes.Add(dequantize);
            }
        }

        graph.Nodes.Clear();
        graph.Nodes.AddRange(newNodes);
        HashSet<string> missing = OnnxGraphEditor.CleanInitializers(graph);
        if (missing.Count > 0)
        {
            throw new System.IO.InvalidDataException(
                "The quantized model asks for tensors nothing provides: " + string.Join(", ", missing) + ".");
        }

        model.Proto.ProducerName = OnnxQuantizationUtilities.ProducerName;
        model.Proto.ProducerVersion = OnnxQuantizationUtilities.ProducerVersion;
        bool hasMicrosoftOpset = false;
        foreach (OnnxOperatorSetId opset in model.Proto.OpsetImports)
        {
            if (string.Equals(opset.Domain, OnnxQuantizationUtilities.MicrosoftDomain, StringComparison.Ordinal))
            {
                hasMicrosoftOpset = true;
                break;
            }
        }

        if (!hasMicrosoftOpset)
        {
            foreach (OnnxNodeProto node in graph.Nodes)
            {
                if (string.Equals(node.Domain, OnnxQuantizationUtilities.MicrosoftDomain, StringComparison.Ordinal))
                {
                    model.Proto.OpsetImports.Add(new OnnxOperatorSetId
                    {
                        Domain = OnnxQuantizationUtilities.MicrosoftDomain,
                        Version = 1,
                    });
                    break;
                }
            }
        }

        OnnxGraphEditor.TopologicalSort(graph);
    }

    /// <summary>
    /// Writes an explicit default location on every initializer large enough that upstream's own round trip would have
    /// moved it to a side file and read it back.
    /// </summary>
    /// <remarks>
    /// Upstream saves the graph and reloads it right after rewriting Gemm, to pick up the shapes that rewrite changes.
    /// It saves WITH external data, so every initializer whose raw bytes reach the ONNX package's threshold goes out to
    /// a side file and comes back with <c>data_location</c> set to its default rather than absent - and that zero is
    /// then written into the quantized model. The managed engine keeps the graph in memory and has no reason to make
    /// that trip, so it writes the same zero here instead; without it every surviving weight of any size differs by two
    /// bytes from what the Python engine writes. The threshold is the ONNX package's own 1024, measured the way that
    /// package measures it - the size of the whole Python bytes object, which is the data plus its 33-byte header.
    /// </remarks>
    /// <param name="graph">The graph whose initializers are marked.</param>
    private static void MarkInitializersThatHaveBeenThroughASideFile(OnnxGraphProto graph)
    {
        foreach (OnnxTensorProto initializer in graph.Initializers)
        {
            if (initializer.RawData != null
                && initializer.RawData.Length >= OnnxQuantizationUtilities.ExternalDataRawSizeThreshold)
            {
                initializer.DataLocation = (int)OnnxDataLocation.Default;
            }
        }
    }

    private static void RejectNonConstantGemmWeights(OnnxGraphProto graph)
    {
        foreach (OnnxNodeProto node in graph.Nodes)
        {
            if (!string.Equals(node.OpType, "Gemm", StringComparison.Ordinal))
            {
                continue;
            }

            if (OnnxQuantizationUtilities.GetIntAttribute(node, "transB", 0) == 1
                && graph.FindInitializer(node.Inputs[1]) == null)
            {
                throw new NotSupportedException(
                    $"The Gemm node '{node.Name}' has a transposed B side that is not an initializer, which the "
                    + "managed dynamic engine does not rewrite. Use the Python engine for this model.");
            }
        }
    }

    private static Dictionary<string, int> BuildValueTypes(OnnxGraphProto graph)
    {
        Dictionary<string, int> types = new Dictionary<string, int>(StringComparer.Ordinal);
        AddValueTypes(types, graph.ValueInfos);
        AddValueTypes(types, graph.Outputs);
        AddValueTypes(types, graph.Inputs);
        return types;
    }

    private static void AddValueTypes(Dictionary<string, int> types, List<OnnxValueInfoProto> values)
    {
        foreach (OnnxValueInfoProto value in values)
        {
            if (value.Name == null || value.Type == null || value.Type.TensorType == null
                || !value.Type.TensorType.ElementType.HasValue)
            {
                continue;
            }

            types[value.Name] = value.Type.TensorType.ElementType.Value;
        }
    }

    private static void AddInferredValueInfos(OnnxGraphProto graph, Dictionary<string, int> types)
    {
        foreach (OnnxNodeProto node in graph.Nodes)
        {
            if (!string.Equals(node.OpType, "MatMul", StringComparison.Ordinal))
            {
                continue;
            }

            string name = node.Outputs[0];
            if (types.ContainsKey(name))
            {
                continue;
            }

            OnnxTensorShapeDimension rows = FindFirstDimension(graph, node.Inputs[0]);
            OnnxTensorShapeDimension columns = FindLastDimension(graph, node.Inputs[1]);
            int elementType = ResolveElementType(graph, types, node.Inputs[0], node.Inputs[1]);
            if (rows == null || columns == null || elementType == 0)
            {
                throw new NotSupportedException(
                    $"The managed dynamic engine cannot work out the type or shape of '{name}' without running shape "
                    + "inference. Use the Python engine for this model.");
            }

            OnnxTensorShapeProto shape = new OnnxTensorShapeProto();
            shape.Dimensions.Add(new OnnxTensorShapeDimension
            {
                DimensionValue = rows.DimensionValue,
                DimensionParameter = rows.DimensionParameter,
            });
            shape.Dimensions.Add(new OnnxTensorShapeDimension
            {
                DimensionValue = columns.DimensionValue,
                DimensionParameter = columns.DimensionParameter,
            });
            graph.ValueInfos.Add(new OnnxValueInfoProto
            {
                Name = name,
                Type = new OnnxTypeProto
                {
                    TensorType = new OnnxTensorTypeProto
                    {
                        ElementType = elementType,
                        Shape = shape,
                    },
                },
            });
            types[name] = elementType;
        }
    }

    private static int ResolveElementType(
        OnnxGraphProto graph,
        Dictionary<string, int> types,
        string first,
        string second)
    {
        if (types.TryGetValue(first, out int type))
        {
            return type;
        }

        OnnxTensorProto initializer = graph.FindInitializer(second);
        if (initializer != null && initializer.DataType.HasValue)
        {
            return initializer.DataType.Value;
        }

        return 0;
    }

    private static OnnxTensorShapeDimension FindFirstDimension(OnnxGraphProto graph, string name)
    {
        OnnxTensorShapeProto shape = FindShape(graph, name);
        return shape != null && shape.Dimensions.Count > 0 ? shape.Dimensions[0] : null;
    }

    private static OnnxTensorShapeDimension FindLastDimension(OnnxGraphProto graph, string name)
    {
        OnnxTensorProto initializer = graph.FindInitializer(name);
        if (initializer != null && initializer.Dimensions.Count > 0)
        {
            return new OnnxTensorShapeDimension
            {
                DimensionValue = initializer.Dimensions[initializer.Dimensions.Count - 1],
            };
        }

        OnnxTensorShapeProto shape = FindShape(graph, name);
        return shape != null && shape.Dimensions.Count > 0 ? shape.Dimensions[shape.Dimensions.Count - 1] : null;
    }

    private static OnnxTensorShapeProto FindShape(OnnxGraphProto graph, string name)
    {
        foreach (List<OnnxValueInfoProto> values in new[] { graph.Inputs, graph.ValueInfos, graph.Outputs })
        {
            foreach (OnnxValueInfoProto value in values)
            {
                if (string.Equals(value.Name, name, StringComparison.Ordinal)
                    && value.Type != null
                    && value.Type.TensorType != null)
                {
                    return value.Type.TensorType.Shape;
                }
            }
        }

        return null;
    }

    private static bool ShouldQuantizeMatMul(
        OnnxNodeProto node,
        OnnxGraphProto graph,
        Dictionary<string, int> types,
        OnnxDynamicQuantizationOptions options)
    {
        if (!string.Equals(node.OpType, "MatMul", StringComparison.Ordinal)
            || !options.OpTypesToQuantize.Contains("MatMul"))
        {
            return false;
        }

        if (options.NodesToQuantize.Count > 0 && (node.Name == null || !options.NodesToQuantize.Contains(node.Name)))
        {
            return false;
        }

        if (node.Name != null && options.NodesToExclude.Contains(node.Name))
        {
            return false;
        }

        if (!IsFloatTensor(node.Inputs[1], graph, types) && !IsFloatTensor(node.Inputs[0], graph, types))
        {
            return false;
        }

        return graph.FindInitializer(node.Inputs[1]) != null;
    }

    private static bool IsFloatTensor(string name, OnnxGraphProto graph, Dictionary<string, int> types)
    {
        OnnxTensorProto initializer = graph.FindInitializer(name);
        if (initializer != null)
        {
            OnnxTensorDataType type = (OnnxTensorDataType)(initializer.DataType ?? 0);
            return type == OnnxTensorDataType.Float || type == OnnxTensorDataType.Float16;
        }

        if (types.TryGetValue(name, out int elementType))
        {
            return elementType == (int)OnnxTensorDataType.Float || elementType == (int)OnnxTensorDataType.Float16;
        }

        return false;
    }

    private static void QuantizeMatMul(
        OnnxNodeProto node,
        OnnxGraphProto graph,
        Dictionary<string, int> types,
        Dictionary<string, OnnxQuantizedValue> quantized,
        List<OnnxNodeProto> newNodes,
        OnnxDynamicQuantizationOptions options)
    {
        List<OnnxNodeProto> nodes = new List<OnnxNodeProto>();
        OnnxQuantizedValue activation = QuantizeActivation(node.Inputs[0], quantized, nodes);
        OnnxQuantizedValue weight = QuantizeWeight(node.Inputs[1], graph, quantized, options);

        string matMulIntegerOutput = node.Outputs[0] + "_output_quantized";
        string matMulIntegerName = string.IsNullOrEmpty(node.Name) ? string.Empty : node.Name + "_quant";
        OnnxNodeProto matMulInteger = new OnnxNodeProto
        {
            OpType = "MatMulInteger",
            Name = matMulIntegerName,
        };
        matMulInteger.Inputs.Add(activation.QuantizedName);
        matMulInteger.Inputs.Add(weight.QuantizedName);
        matMulInteger.Inputs.Add(activation.ZeroPointName);
        matMulInteger.Inputs.Add(weight.ZeroPointName);
        matMulInteger.Outputs.Add(matMulIntegerOutput);
        nodes.Add(matMulInteger);

        string castOutput = matMulIntegerOutput + "_cast_output";
        if (!types.TryGetValue(node.Outputs[0], out int outputType) || outputType == 0)
        {
            throw new System.IO.InvalidDataException(
                $"The type of '{node.Outputs[0]}' is unknown, so the Cast that follows MatMulInteger cannot be written.");
        }

        OnnxNodeProto cast = new OnnxNodeProto
        {
            OpType = "Cast",
            Name = matMulIntegerOutput + "_cast",
        };
        cast.Inputs.Add(matMulIntegerOutput);
        cast.Outputs.Add(castOutput);
        cast.Attributes.Add(OnnxAttributeProto.CreateInt("to", outputType));
        nodes.Add(cast);

        string scalesMulName = matMulIntegerName.Length > 0
            ? matMulIntegerName + "_scales_mul"
            : activation.ScaleName + "_" + weight.ScaleName + "_mul";
        OnnxNodeProto scalesMul = FindNode(scalesMulName, newNodes, nodes, graph);
        if (scalesMul == null)
        {
            scalesMul = new OnnxNodeProto { OpType = "Mul", Name = scalesMulName };
            scalesMul.Inputs.Add(activation.ScaleName);
            scalesMul.Inputs.Add(weight.ScaleName);
            scalesMul.Outputs.Add(scalesMulName + ":0");
            nodes.Add(scalesMul);
        }

        OnnxNodeProto outputMul = new OnnxNodeProto
        {
            OpType = "Mul",
            Name = matMulIntegerName.Length > 0 ? matMulIntegerName + "_output_scale_mul" : string.Empty,
        };
        outputMul.Inputs.Add(castOutput);
        outputMul.Inputs.Add(scalesMul.Outputs[0]);
        outputMul.Outputs.Add(node.Outputs[0]);
        nodes.Add(outputMul);

        newNodes.AddRange(nodes);
    }

    private static OnnxQuantizedValue QuantizeActivation(
        string name,
        Dictionary<string, OnnxQuantizedValue> quantized,
        List<OnnxNodeProto> nodes)
    {
        if (quantized.TryGetValue(name, out OnnxQuantizedValue existing))
        {
            return existing;
        }

        OnnxQuantizedValue value = new OnnxQuantizedValue
        {
            QuantizedName = name + OnnxQuantizationUtilities.TensorNameQuantSuffix,
            ScaleName = name + "_scale",
            ZeroPointName = name + "_zero_point",
        };
        OnnxNodeProto node = new OnnxNodeProto
        {
            OpType = "DynamicQuantizeLinear",
            Name = name + "_QuantizeLinear",
        };
        node.Inputs.Add(name);
        node.Outputs.Add(value.QuantizedName);
        node.Outputs.Add(value.ScaleName);
        node.Outputs.Add(value.ZeroPointName);
        nodes.Add(node);
        quantized[name] = value;
        return value;
    }

    private static OnnxQuantizedValue QuantizeWeight(
        string name,
        OnnxGraphProto graph,
        Dictionary<string, OnnxQuantizedValue> quantized,
        OnnxDynamicQuantizationOptions options)
    {
        if (quantized.TryGetValue(name, out OnnxQuantizedValue existing))
        {
            return existing;
        }

        OnnxTensorProto weight = graph.FindInitializer(name);
        if (weight.HasExternalData)
        {
            throw new NotSupportedException(
                $"The weight '{name}' is still in a side file; read the model's external data first.");
        }

        OnnxTensorDataType weightType = (OnnxTensorDataType)(weight.DataType ?? 0);
        bool halfPrecision = weightType == OnnxTensorDataType.Float16;
        float[] values = OnnxQuantizationUtilities.ReadFloatElements(weight, weight.RawData ?? Array.Empty<byte>());
        bool symmetric = OnnxQuantizationUtilities.IsWeightSymmetric(options.WeightType);
        OnnxQuantizedValue value = new OnnxQuantizedValue
        {
            QuantizedName = name + OnnxQuantizationUtilities.TensorNameQuantSuffix,
            ScaleName = name + "_scale",
            ZeroPointName = name + "_zero_point",
        };

        OnnxQuantizationUtilities.GetQuantizedRange(
            options.WeightType,
            options.ReduceRange,
            symmetric,
            out int quantizedMinimum,
            out int quantizedMaximum);

        if (!options.PerChannel)
        {
            float minimum = values.Length > 0 ? Minimum(values) : 0f;
            float maximum = values.Length > 0 ? Maximum(values) : 0f;
            float scale = OnnxQuantizationUtilities.ComputeScaleAndZeroPoint(
                minimum,
                maximum,
                quantizedMinimum,
                quantizedMaximum,
                symmetric,
                halfPrecision,
                out int zeroPoint);
            int[] elements = OnnxQuantizationUtilities.QuantizeArray(options.WeightType, values, scale, zeroPoint);
            graph.Initializers.Add(OnnxQuantizationUtilities.MakeFloatTensor(
                value.ScaleName,
                weightType,
                Array.Empty<long>(),
                new[] { scale }));
            graph.Initializers.Add(OnnxQuantizationUtilities.MakeInt32Tensor(
                value.ZeroPointName,
                options.WeightType,
                Array.Empty<long>(),
                new[] { zeroPoint }));
            graph.Initializers.Add(OnnxQuantizationUtilities.MakeRawTensor(
                value.QuantizedName,
                options.WeightType,
                weight.Dimensions,
                ToBytes(elements, options.WeightType)));
            quantized[name] = value;
            return value;
        }

        OnnxQuantizationUtilities.Require(
            weight.Dimensions.Count == 2,
            $"Per-channel dynamic quantization needs a rank-two weight; '{name}' has {weight.Dimensions.Count} dimensions.");
        int rows = checked((int)weight.Dimensions[0]);
        int columns = checked((int)weight.Dimensions[1]);
        float[] scales = new float[columns];
        int[] zeroPoints = new int[columns];
        int[] quantizedValues = new int[values.Length];
        float[] channel = new float[rows];
        for (int column = 0; column < columns; column++)
        {
            for (int row = 0; row < rows; row++)
            {
                channel[row] = values[(row * columns) + column];
            }

            float scale = OnnxQuantizationUtilities.ComputeScaleAndZeroPoint(
                Minimum(channel),
                Maximum(channel),
                quantizedMinimum,
                quantizedMaximum,
                symmetric,
                halfPrecision,
                out int zeroPoint);
            scales[column] = scale;
            zeroPoints[column] = zeroPoint;
            int[] elements = OnnxQuantizationUtilities.QuantizeArray(options.WeightType, channel, scale, zeroPoint);
            for (int row = 0; row < rows; row++)
            {
                quantizedValues[(row * columns) + column] = elements[row];
            }
        }

        graph.Initializers.Add(OnnxQuantizationUtilities.MakeFloatTensor(
            value.ScaleName,
            weightType,
            new long[] { columns },
            scales));
        graph.Initializers.Add(OnnxQuantizationUtilities.MakeInt32Tensor(
            value.ZeroPointName,
            options.WeightType,
            new long[] { columns },
            zeroPoints));
        graph.Initializers.Add(OnnxQuantizationUtilities.MakeRawTensor(
            value.QuantizedName,
            options.WeightType,
            weight.Dimensions,
            ToBytes(quantizedValues, options.WeightType)));
        value.Axis = 1;
        quantized[name] = value;
        return value;
    }

    private static void DequantizeInputs(
        OnnxNodeProto node,
        OnnxGraphProto graph,
        Dictionary<string, OnnxQuantizedValue> quantized,
        HashSet<string> generated,
        List<OnnxNodeProto> newNodes)
    {
        foreach (string input in node.Inputs)
        {
            OnnxNodeProto dequantize = BuildDequantizeNode(input, quantized, generated, graph, newNodes);
            if (dequantize != null)
            {
                newNodes.Add(dequantize);
            }
        }
    }

    private static OnnxNodeProto BuildDequantizeNode(
        string name,
        Dictionary<string, OnnxQuantizedValue> quantized,
        HashSet<string> generated,
        OnnxGraphProto graph,
        List<OnnxNodeProto> newNodes)
    {
        if (name == null || !quantized.TryGetValue(name, out OnnxQuantizedValue value) || generated.Contains(name))
        {
            return null;
        }

        string dequantizeName = name + "_DequantizeLinear";
        if (FindNode(dequantizeName, newNodes, null, graph) != null)
        {
            return null;
        }

        OnnxNodeProto node = new OnnxNodeProto
        {
            OpType = "DequantizeLinear",
            Name = dequantizeName,
        };
        node.Inputs.Add(value.QuantizedName);
        node.Inputs.Add(value.ScaleName);
        node.Inputs.Add(value.ZeroPointName);
        node.Outputs.Add(name);
        if (value.Axis.HasValue)
        {
            node.Attributes.Add(OnnxAttributeProto.CreateInt("axis", value.Axis.Value));
        }

        return node;
    }

    private static OnnxNodeProto FindNode(
        string name,
        List<OnnxNodeProto> newNodes,
        List<OnnxNodeProto> pending,
        OnnxGraphProto graph)
    {
        foreach (OnnxNodeProto node in graph.Nodes)
        {
            if (string.Equals(node.Name, name, StringComparison.Ordinal))
            {
                return node;
            }
        }

        foreach (OnnxNodeProto node in newNodes)
        {
            if (string.Equals(node.Name, name, StringComparison.Ordinal))
            {
                return node;
            }
        }

        if (pending != null)
        {
            foreach (OnnxNodeProto node in pending)
            {
                if (string.Equals(node.Name, name, StringComparison.Ordinal))
                {
                    return node;
                }
            }
        }

        return null;
    }

    private static byte[] ToBytes(int[] values, OnnxTensorDataType type)
    {
        byte[] bytes = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            bytes[i] = type == OnnxTensorDataType.Int8 ? unchecked((byte)(sbyte)values[i]) : (byte)values[i];
        }

        return bytes;
    }

    private static float Minimum(ReadOnlySpan<float> values)
    {
        float minimum = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < minimum)
            {
                minimum = values[i];
            }
        }

        return minimum;
    }

    private static float Maximum(ReadOnlySpan<float> values)
    {
        float maximum = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > maximum)
            {
                maximum = values[i];
            }
        }

        return maximum;
    }
}
