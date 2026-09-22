using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>FP32 hyperbolic tangent with a portable implementation on every supported processor.</summary>
internal sealed class OnnxTanhKernel : OnnxKernel, IOnnxUnaryOperation<float>, IOnnxUnaryVectorOperation
{
    internal override string OpType => "Tanh";
    public static bool CanVectorize => false;
    public static float Apply(float value) => MathF.Tanh(value);
    public static Vector<float> Apply(Vector<float> value) => throw new InvalidOperationException("Tanh uses the scalar elementwise path.");
    internal override void Run(OnnxOperatorContext context) => OnnxElementwise.UnaryFloat<OnnxTanhKernel>(context);
}
