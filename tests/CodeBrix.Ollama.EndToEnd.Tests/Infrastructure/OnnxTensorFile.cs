namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>One tensor the oracle script wrote: its name, element type, shape and the file holding it.</summary>
public sealed class OnnxTensorFile
{
    /// <summary>The name the graph knows the tensor by.</summary>
    public string Name { get; set; }

    /// <summary>The element type, spelled as the script spells it: float, int64, int32 or bool.</summary>
    public string Type { get; set; }

    /// <summary>The shape, outermost dimension first.</summary>
    public long[] Shape { get; set; }

    /// <summary>The file holding the elements, raw and little-endian, relative to the step's folder.</summary>
    public string File { get; set; }
}
