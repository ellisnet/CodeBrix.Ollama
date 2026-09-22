namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Shape validation for internal caller-owned output buffers.</summary>
internal static class OnnxOutputBinding
{
    internal static void Validate(OnnxTensor target, OnnxElementType type, long[] shape)
    {
        if (target.ElementType != type || target.Shape.Count != shape.Length)
            throw new InferenceException("A bound ONNX output has the wrong type or shape.");
        for (int i = 0; i < shape.Length; i++)
            if (target.Shape[i] != shape[i]) throw new InferenceException("A bound ONNX output has the wrong shape.");
    }
}
