namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One graph the engine is required to turn away when it loads it, and a fragment of what it should say.
/// </summary>
public sealed class OnnxRefusalCase
{
    /// <summary>The file's name, without its extension.</summary>
    public string Name { get; set; }

    /// <summary>Text the refusal's message has to contain, so a silent or vague refusal fails too.</summary>
    public string Expect { get; set; }
}
