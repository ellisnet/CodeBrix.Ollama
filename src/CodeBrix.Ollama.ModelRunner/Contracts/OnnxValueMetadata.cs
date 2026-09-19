using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a graph says about one of its inputs or outputs: the name a run refers to it by, the element type it
/// carries, and its declared shape.
/// </summary>
public sealed class OnnxValueMetadata
{
    /// <summary>Builds the description of one graph input or output.</summary>
    /// <param name="name">The name.</param>
    /// <param name="elementType">The element type.</param>
    /// <param name="shape">The declared shape, which may be empty when the file states none.</param>
    internal OnnxValueMetadata(string name, OnnxElementType elementType, IReadOnlyList<OnnxDimension> shape)
    {
        Name = name;
        ElementType = elementType;
        Shape = shape;
    }

    /// <summary>The name a run passes this tensor under.</summary>
    public string Name { get; }

    /// <summary>The element type the graph declares.</summary>
    public OnnxElementType ElementType { get; }

    /// <summary>
    /// The declared shape, outermost dimension first. It is empty when the file declares no shape at all,
    /// which the specification allows and which means nothing is promised about the rank.
    /// </summary>
    public IReadOnlyList<OnnxDimension> Shape { get; }

    /// <summary>Returns the name, the type and the declared shape.</summary>
    /// <returns>A one-line description, for example <c>past_key_values.0.key: float[batch,4,past_seq,256]</c>.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder();
        text.Append(Name);
        text.Append(": ");
        text.Append(OnnxTensor.Name(ElementType));
        text.Append('[');
        for (int i = 0; i < Shape.Count; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(Shape[i].ToString());
        }

        text.Append(']');
        return text.ToString();
    }
}
