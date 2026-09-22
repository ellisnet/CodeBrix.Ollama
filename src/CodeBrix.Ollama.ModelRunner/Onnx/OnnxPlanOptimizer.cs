using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Combines single-consumer right-hand transposes and scales with their matrix multiplication.</summary>
/// <remarks>
/// Only the last two axes may be swapped. Graph outputs and tensors read elsewhere are never removed.
/// The fused kernel checks dynamic shapes and uses the original operations when a scale is not scalar.
/// Plans that quantize activations keep their original floating-point accumulation order: a small rounding
/// change can cross a quantization threshold and change later integer products by a whole count.
/// </remarks>
internal static class OnnxPlanOptimizer
{
    internal static OnnxPlanNode[] Optimize(OnnxPlanSlot[] slots, OnnxPlanNode[] nodes)
    {
        foreach (OnnxPlanNode node in nodes)
            if (node.Kernel is OnnxDynamicQuantizeLinearKernel) return nodes;

        int[] readers = new int[slots.Length];
        foreach (OnnxPlanNode node in nodes)
        {
            foreach (int input in node.Inputs) if (input >= 0) readers[input]++;
        }

        bool[] removed = new bool[nodes.Length];
        OnnxPlanNode[] replacements = (OnnxPlanNode[])nodes.Clone();
        foreach (OnnxPlanNode node in nodes)
        {
            if (node.Kernel is not OnnxMatMulKernel || node.State != null) continue;
            OnnxPlanNode producer = SingleProducer(node.Inputs[1], slots, nodes, readers);
            OnnxPlanNode transpose = producer;
            OnnxPlanNode multiply = null;
            int scaleInput = -1;
            if (producer?.Kernel is OnnxMulKernel)
            {
                multiply = producer;
                for (int i = 0; i < 2; i++)
                {
                    OnnxPlanNode candidate = SingleProducer(producer.Inputs[i], slots, nodes, readers);
                    if (!IsMatrixTranspose(candidate)) continue;
                    transpose = candidate;
                    scaleInput = 1 - i;
                    break;
                }
            }

            if (!IsMatrixTranspose(transpose) || (multiply != null && scaleInput < 0)) continue;

            int[] inputs = multiply == null
                ? new[] { node.Inputs[0], transpose.Inputs[0] }
                : new[] { node.Inputs[0], transpose.Inputs[0], multiply.Inputs[scaleInput] };
            OnnxPlanNode fused = new OnnxPlanNode(
                node.Index, "MatMulTranspose" + (multiply == null ? string.Empty : "Scale"), node.Name,
                new OnnxFusedMatMulKernel(), inputs, node.Outputs)
            {
                State = new OnnxMatMulFusion(transpose, multiply, node, scaleInput),
            };
            fused.OutputIsGraphOutput[0] = node.OutputIsGraphOutput[0];
            replacements[node.Index] = fused;
            removed[transpose.Index] = true;
            if (multiply != null) removed[multiply.Index] = true;
        }

        // Rebuild indices before lifetime analysis, including the producers of every surviving value.
        foreach (OnnxPlanSlot slot in slots)
        {
            if (slot.Kind == OnnxSlotKind.Computed) slot.Producer = -1;
            slot.LastUse = -1;
        }

        List<OnnxPlanNode> result = new List<OnnxPlanNode>(nodes.Length);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (removed[i]) continue;
            OnnxPlanNode old = replacements[i];
            OnnxPlanNode current = new OnnxPlanNode(
                result.Count, old.OpType, old.Name, old.Kernel, old.Inputs, old.Outputs) { State = old.State };
            for (int j = 0; j < current.Outputs.Length; j++)
            {
                int output = current.Outputs[j];
                if (output < 0) continue;
                current.OutputIsGraphOutput[j] = slots[output].IsGraphOutput;
                slots[output].Producer = current.Index;
            }

            result.Add(current);
        }

        return result.ToArray();
    }

    private static OnnxPlanNode SingleProducer(
        int index, OnnxPlanSlot[] slots, OnnxPlanNode[] nodes, int[] readers)
    {
        if (index < 0) return null;
        OnnxPlanSlot slot = slots[index];
        return slot.Kind == OnnxSlotKind.Computed && !slot.IsGraphOutput && readers[index] == 1
            ? nodes[slot.Producer]
            : null;
    }

    private static bool IsMatrixTranspose(OnnxPlanNode node)
    {
        if (node?.Kernel is not OnnxTransposeKernel || node.State is not long[] order || order.Length < 2)
            return false;
        for (int i = 0; i < order.Length; i++)
        {
            long axis = order[i] < 0 ? order[i] + order.Length : order[i];
            int expected = i < order.Length - 2 ? i : (2 * order.Length) - 3 - i;
            if (axis != expected) return false;
        }

        return true;
    }
}
