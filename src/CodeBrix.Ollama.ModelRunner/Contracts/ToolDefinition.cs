namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A tool (function) the model may ask to call.
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>The name the model calls the tool by.</summary>
    public string Name { get; set; }

    /// <summary>What the tool does, for the model to read.</summary>
    public string Description { get; set; }

    /// <summary>The JSON schema of the tool's arguments, as JSON text. An object schema; may be <see langword="null"/> for a tool without arguments.</summary>
    public string ParametersJsonSchema { get; set; }
}
