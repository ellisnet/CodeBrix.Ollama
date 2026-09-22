namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A transposed right operand, optionally scaled, and the original operations for other shapes.</summary>
internal sealed class OnnxMatMulFusion
{
    internal OnnxMatMulFusion(OnnxPlanNode transpose, OnnxPlanNode multiply, OnnxPlanNode matmul, int scaleInput)
    {
        Rank = ((long[])transpose.State).Length;
        Transpose = Copy(transpose, new[] { 1 }, new[] { 3 });
        if (multiply != null)
        {
            Multiply = Copy(multiply, scaleInput == 0 ? new[] { 2, 3 } : new[] { 3, 2 }, new[] { 4 });
        }

        MatMul = Copy(matmul, new[] { 0, multiply == null ? 3 : 4 }, new[] { 5 });
        MatMul.OutputIsGraphOutput[0] = matmul.OutputIsGraphOutput[0];
    }

    internal int Rank { get; }
    internal OnnxPlanNode Transpose { get; }
    internal OnnxPlanNode Multiply { get; }
    internal OnnxPlanNode MatMul { get; }

    private static OnnxPlanNode Copy(OnnxPlanNode node, int[] inputs, int[] outputs) =>
        new OnnxPlanNode(node.Index, node.OpType, node.Name, node.Kernel, inputs, outputs) { State = node.State };
}
