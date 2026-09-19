using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>ConstantOfShape</c>: a tensor of the shape another tensor names, every element the same.
/// </summary>
/// <remarks>
/// The <c>value</c> attribute is a ONE-ELEMENT tensor, and it fixes both the number to fill with and the
/// element type of the result. With no attribute at all the result is a float tensor of zeros, which is what
/// the specification says.
/// </remarks>
internal sealed class OnnxConstantOfShapeKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "ConstantOfShape";

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "value" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        OnnxValue value = context.Tensor("value");
        if (value == null)
        {
            return OnnxValue.Wrap(OnnxElementType.Float, new float[] { 0f }, new long[] { 1 });
        }

        if (value.Count != 1)
        {
            throw context.Refuse(
                "its value holds " + value.Count.ToString(CultureInfo.InvariantCulture)
                + " elements and must hold one");
        }

        return value;
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue value = (OnnxValue)context.State;
        long[] shape = OnnxIntegers.Read(context.RequireInput(0), context, "its shape");
        for (int i = 0; i < shape.Length; i++)
        {
            if (shape[i] < 0)
            {
                throw context.Fail("its shape names a negative dimension.");
            }
        }

        OnnxValue result = context.AllocateOutput(0, value.ElementType, shape);
        int count = result.Count;
        if (count == 0) return;

        switch (value.ElementType)
        {
            case OnnxElementType.Float:
                Array.Fill(result.Floats, value.Floats[0], 0, count);
                return;
            case OnnxElementType.Int64:
                Array.Fill(result.Int64s, value.Int64s[0], 0, count);
                return;
            case OnnxElementType.Int32:
                Array.Fill(result.Int32s, value.Int32s[0], 0, count);
                return;
            case OnnxElementType.Bool:
                Array.Fill(result.Booleans, value.Booleans[0], 0, count);
                return;
            default:
                throw context.Fail(
                    "it cannot fill a " + OnnxTensor.Name(value.ElementType) + " tensor.");
        }
    }
}
