namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The discriminator of an ONNX <c>AttributeProto</c>, saying which of its value fields is in use.
/// </summary>
internal enum OnnxAttributeType
{
    /// <summary>No value declared.</summary>
    Undefined = 0,

    /// <summary>A single float.</summary>
    Float = 1,

    /// <summary>A single integer.</summary>
    Int = 2,

    /// <summary>A single string.</summary>
    String = 3,

    /// <summary>A single tensor.</summary>
    Tensor = 4,

    /// <summary>A single graph.</summary>
    Graph = 5,

    /// <summary>A single sparse tensor.</summary>
    SparseTensor = 11,

    /// <summary>A single type.</summary>
    TypeProto = 13,

    /// <summary>A list of floats.</summary>
    Floats = 6,

    /// <summary>A list of integers.</summary>
    Ints = 7,

    /// <summary>A list of strings.</summary>
    Strings = 8,

    /// <summary>A list of tensors.</summary>
    Tensors = 9,

    /// <summary>A list of graphs.</summary>
    Graphs = 10,

    /// <summary>A list of sparse tensors.</summary>
    SparseTensors = 12,

    /// <summary>A list of types.</summary>
    TypeProtos = 14,
}
