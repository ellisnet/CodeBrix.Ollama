namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>The fixture folder's <c>cases.json</c>: every case, and every graph that must be refused.</summary>
public sealed class OnnxFixtureIndex
{
    /// <summary>The names of the cases, one folder each.</summary>
    public string[] Cases { get; set; }

    /// <summary>The graphs that must be refused when they are loaded.</summary>
    public OnnxRefusalCase[] Refusals { get; set; }
}
