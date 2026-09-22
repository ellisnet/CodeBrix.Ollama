using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>FP32 ONNX Elu, preserving ONNX Runtime's float exp-minus-one rounding.</summary>
internal sealed class OnnxEluKernel : OnnxKernel
{
    internal override string OpType => "Elu";
    internal override string[] Attributes => new[] { "alpha" };
    internal override object Prepare(OnnxNodeLoadContext context) => context.Float("alpha", 1f);

    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        if (input.ElementType != OnnxElementType.Float)
        {
            throw context.Fail("Elu requires float input.");
        }
        OnnxValue output = context.AllocateOutput(0, OnnxElementType.Float, (long[])input.Shape.Clone());
        float alpha = (float)context.State;
        for (int i = 0; i < input.Count; i++)
        {
            float value = input.Floats[i];
            output.Floats[i] = value >= 0f ? value : alpha * (MathF.Exp(value) - 1f);
        }
    }
}
