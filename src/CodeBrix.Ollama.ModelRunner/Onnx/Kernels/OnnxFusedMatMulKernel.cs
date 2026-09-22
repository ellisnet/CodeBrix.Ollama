namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Reads a right-hand matrix in its original layout, applying its scale while multiplying.</summary>
internal sealed class OnnxFusedMatMulKernel : OnnxKernel
{
    internal override string OpType => "MatMulTransposeScale";

    internal override void Run(OnnxOperatorContext context)
    {
        OnnxMatMulFusion fusion = (OnnxMatMulFusion)context.State;
        OnnxValue left = context.RequireInput(0);
        OnnxValue right = context.RequireInput(1);
        OnnxValue scale = fusion.Multiply == null ? null : context.RequireInput(2);
        if (right.Rank == fusion.Rank && right.ElementType == OnnxElementType.Float
            && left.ElementType == OnnxElementType.Float
            && (scale == null || (scale.ElementType == OnnxElementType.Float
                && scale.Count == 1 && scale.Rank <= right.Rank)))
        {
            OnnxMatMulKernel.RunGeneral(context, left, right, true, scale?.Floats[0]);
            return;
        }

        // A dynamically shaped multiplier may broadcast a vector or introduce an axis. Preserve the full
        // original semantics (and validation) for that case instead of assuming this is always attention.
        OnnxValue[] values = { left, right, scale, null, null, null };
        OnnxOperatorContext fallback = new OnnxOperatorContext(values, context.Arena, context.Settings);
        try
        {
            fallback.Bind(fusion.Transpose);
            fusion.Transpose.Kernel.Run(fallback);
            if (fusion.Multiply != null)
            {
                fallback.Bind(fusion.Multiply);
                fusion.Multiply.Kernel.Run(fallback);
            }

            fallback.Bind(fusion.MatMul);
            fusion.MatMul.Kernel.Run(fallback);
            context.SetOutput(0, values[5]);
            values[5] = null;
        }
        finally
        {
            for (int i = 3; i < values.Length; i++) values[i]?.Release(context.Arena);
        }
    }
}
