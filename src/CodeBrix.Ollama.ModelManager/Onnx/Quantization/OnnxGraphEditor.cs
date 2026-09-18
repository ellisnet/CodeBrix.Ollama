// --------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.
// --------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/python/tools/quantization/onnx_model.py@v1.30.0

/// <summary>
/// The graph surgery ONNX Runtime's <c>ONNXModel</c> performs around quantization: replacing a plain Gemm with a
/// MatMul and an Add, dropping initializers nothing consumes any more, setting an operator-set version, and the
/// topological sort that decides the node order of the file it writes.
/// </summary>
internal static class OnnxGraphEditor
{
    /// <summary>Sets an operator set's version, adding the import when the model does not already carry it.</summary>
    /// <param name="model">The model to change.</param>
    /// <param name="domain">The operator set's domain.</param>
    /// <param name="version">The version to record.</param>
    internal static void SetOpsetImport(OnnxModelProto model, string domain, long version)
    {
        foreach (OnnxOperatorSetId opset in model.OpsetImports)
        {
            if (string.Equals(opset.Domain ?? string.Empty, domain, StringComparison.Ordinal))
            {
                opset.Version = version;
                return;
            }
        }

        model.OpsetImports.Add(new OnnxOperatorSetId { Domain = domain, Version = version });
    }

    /// <summary>The version of the default ONNX operator set the model imports.</summary>
    /// <param name="model">The model to read.</param>
    /// <returns>The version.</returns>
    internal static long GetOpsetVersion(OnnxModelProto model)
    {
        long version = -1;
        int found = 0;
        foreach (OnnxOperatorSetId opset in model.OpsetImports)
        {
            string domain = opset.Domain ?? string.Empty;
            if (domain.Length == 0 || string.Equals(domain, "ai.onnx", StringComparison.Ordinal))
            {
                found++;
                version = opset.Version ?? 0;
            }
        }

        OnnxQuantizationUtilities.Require(found == 1, "The ONNX model does not import exactly one ai.onnx operator set.");
        return version;
    }

    /// <summary>
    /// Replaces every Gemm whose alpha, beta and transA leave it equivalent to a matrix product with a MatMul, and an
    /// Add when the Gemm carried a bias. A transposed B that is an initializer is transposed in place; one that is not
    /// gains a Transpose node.
    /// </summary>
    /// <param name="graph">The graph to change.</param>
    internal static void ReplaceGemmWithMatMul(OnnxGraphProto graph)
    {
        List<OnnxNodeProto> newNodes = new List<OnnxNodeProto>();
        foreach (OnnxNodeProto node in graph.Nodes)
        {
            if (!string.Equals(node.OpType, "Gemm", StringComparison.Ordinal))
            {
                newNodes.Add(node);
                continue;
            }

            float alpha = OnnxQuantizationUtilities.GetFloatAttribute(node, "alpha", 1.0f);
            float beta = OnnxQuantizationUtilities.GetFloatAttribute(node, "beta", 1.0f);
            long transA = OnnxQuantizationUtilities.GetIntAttribute(node, "transA", 0);
            long transB = OnnxQuantizationUtilities.GetIntAttribute(node, "transB", 0);
            if (alpha != 1.0f || beta != 1.0f || transA != 0)
            {
                newNodes.Add(node);
                continue;
            }

            string inputB = node.Inputs[1];
            if (transB == 1)
            {
                OnnxTensorProto weight = graph.FindInitializer(inputB);
                if (weight != null)
                {
                    OnnxTensorProto transposed = TransposeInitializer(weight);
                    graph.Initializers.Remove(weight);
                    OnnxValueInfoProto declared = graph.FindInput(inputB);
                    if (declared != null)
                    {
                        graph.Inputs.Remove(declared);
                    }

                    graph.Initializers.Add(transposed);
                }
                else
                {
                    inputB += "_Transposed";
                    OnnxNodeProto transpose = new OnnxNodeProto
                    {
                        OpType = "Transpose",
                        Name = string.IsNullOrEmpty(node.Name) ? string.Empty : node.Name + "_Transpose",
                    };
                    transpose.Inputs.Add(node.Inputs[1]);
                    transpose.Outputs.Add(inputB);
                    newNodes.Add(transpose);
                }
            }

            OnnxNodeProto matMul = new OnnxNodeProto
            {
                OpType = "MatMul",
                Name = string.IsNullOrEmpty(node.Name) ? string.Empty : node.Name + "_MatMul",
            };
            matMul.Inputs.Add(node.Inputs[0]);
            matMul.Inputs.Add(inputB);
            matMul.Outputs.Add(node.Inputs.Count > 2 ? node.Outputs[0] + "_MatMul" : node.Outputs[0]);
            newNodes.Add(matMul);

            if (node.Inputs.Count > 2)
            {
                OnnxNodeProto add = new OnnxNodeProto
                {
                    OpType = "Add",
                    Name = string.IsNullOrEmpty(node.Name) ? string.Empty : node.Name + "_Add",
                };
                add.Inputs.Add(node.Outputs[0] + "_MatMul");
                add.Inputs.Add(node.Inputs[2]);
                foreach (string output in node.Outputs)
                {
                    add.Outputs.Add(output);
                }

                newNodes.Add(add);
            }
        }

        graph.Nodes.Clear();
        graph.Nodes.AddRange(newNodes);
    }

    /// <summary>
    /// Drops the initializers no node input and no graph output asks for any more, together with the graph inputs that
    /// declared them.
    /// </summary>
    /// <param name="graph">The graph to clean.</param>
    /// <returns>The names the graph asks for but nothing in it provides.</returns>
    internal static HashSet<string> CleanInitializers(OnnxGraphProto graph)
    {
        HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxNodeProto node in graph.Nodes)
        {
            foreach (string input in node.Inputs)
            {
                if (!string.IsNullOrEmpty(input))
                {
                    requested.Add(input);
                }
            }
        }

        foreach (OnnxValueInfoProto output in graph.Outputs)
        {
            if (!string.IsNullOrEmpty(output.Name))
            {
                requested.Add(output.Name);
            }
        }

        foreach (OnnxNodeProto node in graph.Nodes)
        {
            foreach (string output in node.Outputs)
            {
                requested.Remove(output);
            }
        }

        List<OnnxTensorProto> unused = new List<OnnxTensorProto>();
        foreach (OnnxTensorProto initializer in graph.Initializers)
        {
            if (initializer.Name != null && requested.Contains(initializer.Name))
            {
                requested.Remove(initializer.Name);
            }
            else
            {
                unused.Add(initializer);
            }
        }

        foreach (OnnxTensorProto initializer in unused)
        {
            graph.Initializers.Remove(initializer);
            OnnxValueInfoProto declared = initializer.Name == null ? null : graph.FindInput(initializer.Name);
            if (declared != null)
            {
                graph.Inputs.Remove(declared);
            }
        }

        foreach (OnnxValueInfoProto input in graph.Inputs)
        {
            if (input.Name != null)
            {
                requested.Remove(input.Name);
            }
        }

        return requested;
    }

    /// <summary>
    /// Orders the nodes so every one comes after whatever produces its inputs, resolving ties by the sorted names of
    /// the initializers and graph inputs. This is the order the file ends up in.
    /// </summary>
    /// <param name="graph">The graph to order.</param>
    internal static void TopologicalSort(OnnxGraphProto graph)
    {
        List<OnnxNodeProto> nodes = graph.Nodes;
        int[] dependencies = new int[nodes.Count];
        Dictionary<string, List<int>> consumers = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        List<OnnxNodeProto> sorted = new List<OnnxNodeProto>();
        for (int index = 0; index < nodes.Count; index++)
        {
            OnnxNodeProto node = nodes[index];
            int count = 0;
            foreach (string input in node.Inputs)
            {
                if (!string.IsNullOrEmpty(input))
                {
                    count++;
                }
            }

            dependencies[index] = count;
            if (count == 0)
            {
                sorted.Add(node);
                continue;
            }

            foreach (string input in node.Inputs)
            {
                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                if (!consumers.TryGetValue(input, out List<int> list))
                {
                    list = new List<int>();
                    consumers.Add(input, list);
                }

                list.Add(index);
            }
        }

        List<string> names = new List<string>();
        foreach (OnnxTensorProto initializer in graph.Initializers)
        {
            names.Add(initializer.Name ?? string.Empty);
        }

        foreach (OnnxValueInfoProto input in graph.Inputs)
        {
            names.Add(input.Name ?? string.Empty);
        }

        names.Sort(StringComparer.Ordinal);
        string previous = null;
        foreach (string name in names)
        {
            if (previous != null && string.Equals(previous, name, StringComparison.Ordinal))
            {
                continue;
            }

            previous = name;
            if (!consumers.TryGetValue(name, out List<int> list))
            {
                continue;
            }

            foreach (int index in list)
            {
                dependencies[index]--;
                if (dependencies[index] == 0)
                {
                    sorted.Add(nodes[index]);
                }
            }
        }

        int start = 0;
        int end = sorted.Count;
        while (start < end)
        {
            foreach (string output in sorted[start].Outputs)
            {
                if (!consumers.TryGetValue(output, out List<int> list))
                {
                    continue;
                }

                foreach (int index in list)
                {
                    dependencies[index]--;
                    if (dependencies[index] == 0)
                    {
                        sorted.Add(nodes[index]);
                        end++;
                    }
                }
            }

            start++;
        }

        OnnxQuantizationUtilities.Require(
            end == nodes.Count,
            "The ONNX graph is not acyclic, so its nodes cannot be ordered.");
        graph.Nodes.Clear();
        graph.Nodes.AddRange(sorted);
    }

    /// <summary>The names of the graph inputs that no initializer provides.</summary>
    /// <param name="graph">The graph to read.</param>
    /// <returns>The names.</returns>
    internal static HashSet<string> GetNonInitializerInputs(OnnxGraphProto graph)
    {
        HashSet<string> initializers = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxTensorProto initializer in graph.Initializers)
        {
            if (initializer.Name != null)
            {
                initializers.Add(initializer.Name);
            }
        }

        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxValueInfoProto input in graph.Inputs)
        {
            if (input.Name != null && !initializers.Contains(input.Name))
            {
                result.Add(input.Name);
            }
        }

        return result;
    }

    private static OnnxTensorProto TransposeInitializer(OnnxTensorProto weight)
    {
        OnnxQuantizationUtilities.Require(
            weight.Dimensions.Count == 2,
            $"Only a two-dimensional Gemm weight can be transposed; '{weight.Name}' has {weight.Dimensions.Count} dimensions.");
        OnnxQuantizationUtilities.Require(
            !weight.HasExternalData,
            $"The Gemm weight '{weight.Name}' must be read out of its side file before it can be transposed.");
        int rows = (int)weight.Dimensions[0];
        int columns = (int)weight.Dimensions[1];
        OnnxTensorDataType type = (OnnxTensorDataType)(weight.DataType ?? 0);
        float[] values = OnnxQuantizationUtilities.ReadFloatElements(weight, weight.RawData ?? Array.Empty<byte>());
        float[] transposed = new float[values.Length];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                transposed[(column * rows) + row] = values[(row * columns) + column];
            }
        }

        return OnnxQuantizationUtilities.MakeRawTensor(
            weight.Name,
            type,
            new long[] { columns, rows },
            OnnxQuantizationUtilities.FloatsToRawBytes(transposed, type));
    }
}
