namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One tensor of a fixture case as <c>case.json</c> describes it: its name, its element type, its shape and
/// the file its elements are in.
/// </summary>
public sealed class OnnxFixtureTensor
{
    /// <summary>The name the graph knows the tensor by.</summary>
    public string Name { get; set; }

    /// <summary>The element type, spelled as the generator spells it: float, int64, int32 or bool.</summary>
    public string Type { get; set; }

    /// <summary>The shape, outermost dimension first.</summary>
    public long[] Shape { get; set; }

    /// <summary>The file holding the elements, raw and little-endian, relative to the case's folder.</summary>
    public string File { get; set; }
}
