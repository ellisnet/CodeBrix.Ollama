namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama thinking/parser.go;

/// <summary>
/// Where a <see cref="ThinkingParser"/> has got to in a model's output stream. The states are Ollama's
/// own, in Ollama's order, so a port of Ollama's parser tests can assert on them directly.
/// </summary>
public enum ThinkingParserState
{
    /// <summary>
    /// The opening tag has not been seen yet and no non-whitespace character has arrived either, so an
    /// opening tag is still possible.
    /// </summary>
    LookingForOpening = 0,

    /// <summary>
    /// The opening tag has been seen but no thinking text has arrived yet, so whitespace between the tag
    /// and the reasoning is still being eaten.
    /// </summary>
    ThinkingStartedEatingWhitespace = 1,

    /// <summary>
    /// Thinking text is arriving and the closing tag has not been seen yet.
    /// </summary>
    Thinking = 2,

    /// <summary>
    /// The closing tag has been seen but no content has arrived yet, so whitespace between the tag and the
    /// content is still being eaten.
    /// </summary>
    ThinkingDoneEatingWhitespace = 3,

    /// <summary>
    /// Thinking is over - either it finished or the model never opened a thinking block - and everything
    /// from here on is content.
    /// </summary>
    ThinkingDone = 4,
}
