using System;
using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>FP32 error function using Abramowitz and Stegun 7.1.26 (absolute error below 1.5e-7).</summary>
internal sealed class OnnxErfKernel : OnnxKernel, IOnnxUnaryOperation<float>, IOnnxUnaryVectorOperation
{
    internal override string OpType => "Erf";
    public static bool CanVectorize => true;

    public static float Apply(float value)
    {
        if (value == 0f)
        {
            return value;
        }
        double x = Math.Abs((double)value);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double polynomial = (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t;
        double answer = 1.0 - polynomial * Math.Exp(-x * x);
        return (float)(value < 0f ? -answer : answer);
    }

    public static Vector<float> Apply(Vector<float> value)
    {
        // Keep the same double intermediate precision as the scalar approximation. Vector.Exp is a
        // portable .NET operation and supplies its own fallback where hardware acceleration is absent.
        Vector.Widen(value, out Vector<double> low, out Vector<double> high);
        Vector<float> result = Vector.Narrow(Evaluate(low), Evaluate(high));
        return Vector.ConditionalSelect(Vector.Equals(value, Vector<float>.Zero), value, result);
    }

    private static Vector<double> Evaluate(Vector<double> value)
    {
        Vector<double> x = Vector.Abs(value);
        Vector<double> one = Vector<double>.One;
        Vector<double> t = one / (one + new Vector<double>(0.3275911) * x);
        Vector<double> polynomial = (((((new Vector<double>(1.061405429) * t - new Vector<double>(1.453152027)) * t)
            + new Vector<double>(1.421413741)) * t - new Vector<double>(0.284496736)) * t + new Vector<double>(0.254829592)) * t;
        Vector<double> result = one - polynomial * Vector.Exp(-x * x);
        return Vector.ConditionalSelect(Vector.LessThan(value, Vector<double>.Zero), -result, result);
    }
    internal override void Run(OnnxOperatorContext context) => OnnxElementwise.UnaryFloat<OnnxErfKernel>(context);
}
