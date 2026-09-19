using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Cast</c>: the same numbers in another element type.
/// </summary>
/// <remarks>
/// The target type is refused when the model is LOADED rather than when the node first runs, so a graph that
/// asks for a type this engine does not compute in says so straight away. <c>saturate</c>, which arrived with
/// the 8-bit float types, is not accepted for the same reason: this engine has no 8-bit float to saturate to.
/// </remarks>
internal sealed class OnnxCastKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Cast";

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "to" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        long to = context.Int("to", 0);
        switch ((OnnxTensorDataType)to)
        {
            case OnnxTensorDataType.Float:
                return OnnxElementType.Float;
            case OnnxTensorDataType.Int64:
                return OnnxElementType.Int64;
            case OnnxTensorDataType.Int32:
                return OnnxElementType.Int32;
            case OnnxTensorDataType.Bool:
                return OnnxElementType.Bool;
            case OnnxTensorDataType.UInt8:
                return OnnxElementType.UInt8;
            case OnnxTensorDataType.Int8:
                return OnnxElementType.Int8;
            default:
                throw context.Refuse(
                    "it casts to " + OnnxTensorReader.Describe((OnnxTensorDataType)to)
                    + ", and this engine computes in float, int64, int32, bool and the two quantized 8-bit"
                    + " types");
        }
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        OnnxValue result = context.AllocateOutput(
            0, (OnnxElementType)context.State, (long[])input.Shape.Clone());
        if (result.Count == 0) return;

        OnnxConvert.Convert(input, result);
    }
}
