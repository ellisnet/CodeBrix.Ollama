namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama tools/tools.go;

/// <summary>
/// Where a <see cref="ToolCallParser"/> has got to in a model's output stream.
/// </summary>
public enum ToolCallParserState
{
    /// <summary>
    /// The tool-call prefix has not been seen yet; text is content until it turns up.
    /// </summary>
    LookingForTag = 0,

    /// <summary>
    /// The prefix has been seen and everything from here is being read as tool calls.
    /// </summary>
    ToolCalling = 1,

    /// <summary>
    /// Parsing is over, either because the calls are complete or because the output turned out not to be a
    /// tool call at all. Everything from here is content.
    /// </summary>
    Done = 2,
}
