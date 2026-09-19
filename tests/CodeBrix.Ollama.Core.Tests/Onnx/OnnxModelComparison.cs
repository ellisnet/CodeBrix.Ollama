using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Compares what the managed engine produced with what ONNX Runtime's Python tools produced: the graph structure, the
/// operator sets, the declared values and every initializer's bytes. It reports differences rather than throwing, so
/// a failing test names the first thing that drifted.
/// </summary>
internal static class OnnxModelComparison
{
    /// <summary>Lists every way two models differ.</summary>
    /// <param name="expected">The oracle model.</param>
    /// <param name="actual">The managed model.</param>
    /// <returns>The differences, empty when the models match.</returns>
    internal static IReadOnlyList<string> Compare(OnnxModelProto expected, OnnxModelProto actual)
    {
        List<string> differences = new List<string>();
        CompareScalar(differences, "ir_version", expected.IrVersion, actual.IrVersion);
        CompareScalar(differences, "producer_name", expected.ProducerName, actual.ProducerName);
        CompareScalar(differences, "producer_version", expected.ProducerVersion, actual.ProducerVersion);
        CompareScalar(differences, "model_version", expected.ModelVersion, actual.ModelVersion);
        CompareScalar(differences, "domain", expected.Domain, actual.Domain);
        CompareScalar(differences, "doc_string", expected.DocString, actual.DocString);
        CompareOpsets(differences, expected, actual);
        CompareEntries(differences, "metadata_props", expected.MetadataProperties, actual.MetadataProperties);
        CompareGraphs(differences, expected.Graph, actual.Graph);
        return differences;
    }

    private static void CompareGraphs(List<string> differences, OnnxGraphProto expected, OnnxGraphProto actual)
    {
        if (expected == null || actual == null)
        {
            if (!ReferenceEquals(expected, actual))
            {
                differences.Add("one model has a graph and the other does not");
            }

            return;
        }

        CompareScalar(differences, "graph.name", expected.Name, actual.Name);
        CompareScalar(differences, "graph.doc_string", expected.DocString, actual.DocString);
        if (expected.Nodes.Count != actual.Nodes.Count)
        {
            differences.Add(
                $"node count: expected {expected.Nodes.Count} ({Describe(expected.Nodes)}), "
                + $"actual {actual.Nodes.Count} ({Describe(actual.Nodes)})");
        }
        else
        {
            for (int index = 0; index < expected.Nodes.Count; index++)
            {
                CompareNodes(differences, index, expected.Nodes[index], actual.Nodes[index]);
            }
        }

        CompareInitializers(differences, expected.Initializers, actual.Initializers);
        CompareValues(differences, "graph.input", expected.Inputs, actual.Inputs);
        CompareValues(differences, "graph.output", expected.Outputs, actual.Outputs);
        CompareValues(differences, "graph.value_info", expected.ValueInfos, actual.ValueInfos);
        CompareEntries(differences, "graph.metadata_props", expected.MetadataProperties, actual.MetadataProperties);
    }

    private static void CompareNodes(
        List<string> differences,
        int index,
        OnnxNodeProto expected,
        OnnxNodeProto actual)
    {
        string where = $"node[{index}] '{expected.Name}'";
        CompareScalar(differences, $"{where}.name", expected.Name, actual.Name);
        CompareScalar(differences, $"{where}.op_type", expected.OpType, actual.OpType);
        CompareScalar(differences, $"{where}.domain", expected.Domain, actual.Domain);
        CompareStringLists(differences, $"{where}.input", expected.Inputs, actual.Inputs);
        CompareStringLists(differences, $"{where}.output", expected.Outputs, actual.Outputs);
        if (expected.Attributes.Count != actual.Attributes.Count)
        {
            differences.Add(
                $"{where}.attribute count: expected {expected.Attributes.Count} ({DescribeAttributes(expected)}), "
                + $"actual {actual.Attributes.Count} ({DescribeAttributes(actual)})");
            return;
        }

        for (int attribute = 0; attribute < expected.Attributes.Count; attribute++)
        {
            OnnxAttributeProto left = expected.Attributes[attribute];
            OnnxAttributeProto right = actual.Attributes[attribute];
            CompareScalar(differences, $"{where}.attribute[{attribute}].name", left.Name, right.Name);
            CompareScalar(differences, $"{where}.attribute[{attribute}].type", left.AttributeType, right.AttributeType);
            CompareScalar(differences, $"{where}.attribute[{attribute}].i", left.Int, right.Int);
            CompareScalar(differences, $"{where}.attribute[{attribute}].f", left.Float, right.Float);
        }
    }

    private static void CompareInitializers(
        List<string> differences,
        List<OnnxTensorProto> expected,
        List<OnnxTensorProto> actual)
    {
        if (expected.Count != actual.Count)
        {
            differences.Add(
                $"initializer count: expected {expected.Count} ({DescribeTensors(expected)}), "
                + $"actual {actual.Count} ({DescribeTensors(actual)})");
            return;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            OnnxTensorProto left = expected[index];
            OnnxTensorProto right = actual[index];
            string where = $"initializer[{index}] '{left.Name}'";
            CompareScalar(differences, $"{where}.name", left.Name, right.Name);
            CompareScalar(differences, $"{where}.data_type", left.DataType, right.DataType);
            CompareScalar(differences, $"{where}.dims", Join(left.Dimensions), Join(right.Dimensions));
            CompareBytes(differences, $"{where}.raw_data", left.RawData, right.RawData);
            CompareScalar(differences, $"{where}.float_data", Join(left.FloatData), Join(right.FloatData));
            CompareScalar(differences, $"{where}.int32_data", Join(left.Int32Data), Join(right.Int32Data));
            CompareScalar(differences, $"{where}.int64_data", Join(left.Int64Data), Join(right.Int64Data));
            CompareScalar(differences, $"{where}.data_location", left.DataLocation, right.DataLocation);
            CompareEntries(differences, $"{where}.external_data", left.ExternalData, right.ExternalData);
        }
    }

    private static void CompareValues(
        List<string> differences,
        string where,
        List<OnnxValueInfoProto> expected,
        List<OnnxValueInfoProto> actual)
    {
        if (expected.Count != actual.Count)
        {
            differences.Add(
                $"{where} count: expected {expected.Count} ({DescribeValues(expected)}), "
                + $"actual {actual.Count} ({DescribeValues(actual)})");
            return;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            CompareScalar(differences, $"{where}[{index}].name", expected[index].Name, actual[index].Name);
            CompareScalar(
                differences,
                $"{where}[{index}].type",
                DescribeType(expected[index]),
                DescribeType(actual[index]));
        }
    }

    private static void CompareOpsets(List<string> differences, OnnxModelProto expected, OnnxModelProto actual)
    {
        string left = DescribeOpsets(expected);
        string right = DescribeOpsets(actual);
        if (!string.Equals(left, right, StringComparison.Ordinal))
        {
            differences.Add($"opset_import: expected [{left}], actual [{right}]");
        }
    }

    private static void CompareEntries(
        List<string> differences,
        string where,
        List<OnnxStringStringEntry> expected,
        List<OnnxStringStringEntry> actual)
    {
        string left = DescribeEntries(expected);
        string right = DescribeEntries(actual);
        if (!string.Equals(left, right, StringComparison.Ordinal))
        {
            differences.Add($"{where}: expected [{left}], actual [{right}]");
        }
    }

    private static void CompareStringLists(
        List<string> differences,
        string where,
        List<string> expected,
        List<string> actual)
    {
        string left = string.Join(",", expected);
        string right = string.Join(",", actual);
        if (!string.Equals(left, right, StringComparison.Ordinal))
        {
            differences.Add($"{where}: expected [{left}], actual [{right}]");
        }
    }

    private static void CompareBytes(List<string> differences, string where, byte[] expected, byte[] actual)
    {
        if (expected == null && actual == null)
        {
            return;
        }

        if (expected == null || actual == null)
        {
            differences.Add($"{where}: one side has raw data and the other does not");
            return;
        }

        if (expected.Length != actual.Length)
        {
            differences.Add($"{where}: expected {expected.Length} bytes, actual {actual.Length} bytes");
            return;
        }

        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] != actual[index])
            {
                differences.Add(
                    $"{where}: first differing byte at {index}, expected 0x{expected[index]:x2}, "
                    + $"actual 0x{actual[index]:x2}");
                return;
            }
        }
    }

    private static void CompareScalar<T>(List<string> differences, string where, T expected, T actual)
    {
        string left = expected == null ? "<absent>" : Convert.ToString(expected, CultureInfo.InvariantCulture);
        string right = actual == null ? "<absent>" : Convert.ToString(actual, CultureInfo.InvariantCulture);
        if (!string.Equals(left, right, StringComparison.Ordinal))
        {
            differences.Add($"{where}: expected '{left}', actual '{right}'");
        }
    }

    private static string Describe(List<OnnxNodeProto> nodes)
    {
        List<string> parts = new List<string>();
        foreach (OnnxNodeProto node in nodes)
        {
            parts.Add($"{node.OpType}:{node.Name}");
        }

        return string.Join(",", parts);
    }

    private static string DescribeAttributes(OnnxNodeProto node)
    {
        List<string> parts = new List<string>();
        foreach (OnnxAttributeProto attribute in node.Attributes)
        {
            parts.Add(attribute.Name);
        }

        return string.Join(",", parts);
    }

    private static string DescribeTensors(List<OnnxTensorProto> tensors)
    {
        List<string> parts = new List<string>();
        foreach (OnnxTensorProto tensor in tensors)
        {
            parts.Add(tensor.Name);
        }

        return string.Join(",", parts);
    }

    private static string DescribeValues(List<OnnxValueInfoProto> values)
    {
        List<string> parts = new List<string>();
        foreach (OnnxValueInfoProto value in values)
        {
            parts.Add(value.Name);
        }

        return string.Join(",", parts);
    }

    private static string DescribeOpsets(OnnxModelProto model)
    {
        List<string> parts = new List<string>();
        foreach (OnnxOperatorSetId opset in model.OpsetImports)
        {
            parts.Add($"{opset.Domain ?? string.Empty}={opset.Version}");
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join(",", parts);
    }

    private static string DescribeEntries(List<OnnxStringStringEntry> entries)
    {
        List<string> parts = new List<string>();
        foreach (OnnxStringStringEntry entry in entries)
        {
            parts.Add($"{entry.Key}={entry.Value}");
        }

        return string.Join(",", parts);
    }

    private static string DescribeType(OnnxValueInfoProto value)
    {
        if (value.Type == null || value.Type.TensorType == null)
        {
            return "<absent>";
        }

        OnnxTensorTypeProto tensorType = value.Type.TensorType;
        List<string> dimensions = new List<string>();
        if (tensorType.Shape != null)
        {
            foreach (OnnxTensorShapeDimension dimension in tensorType.Shape.Dimensions)
            {
                dimensions.Add(dimension.DimensionParameter
                    ?? (dimension.DimensionValue.HasValue
                        ? dimension.DimensionValue.Value.ToString(CultureInfo.InvariantCulture)
                        : "?"));
            }
        }

        return $"{tensorType.ElementType}[{string.Join(",", dimensions)}]";
    }

    private static string Join<T>(List<T> values)
    {
        List<string> parts = new List<string>();
        foreach (T value in values)
        {
            parts.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        return string.Join(",", parts);
    }
}
