namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Identity</c>: hands its input straight back.
/// </summary>
/// <remarks>
/// Nothing is copied. The result is a second description of the same elements, and the buffer goes back to
/// the arena only when both descriptions have been finished with.
/// </remarks>
internal sealed class OnnxIdentityKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Identity";

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        context.SetOutput(0, input.Reshaped((long[])input.Shape.Clone()));
    }
}
