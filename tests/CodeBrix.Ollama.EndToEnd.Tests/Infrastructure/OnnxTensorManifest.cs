namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>The <c>manifest.json</c> the oracle script writes beside one step's tensors.</summary>
public sealed class OnnxTensorManifest
{
    /// <summary>The tensors, in the order the graph declares them.</summary>
    public OnnxTensorFile[] Tensors { get; set; }
}
