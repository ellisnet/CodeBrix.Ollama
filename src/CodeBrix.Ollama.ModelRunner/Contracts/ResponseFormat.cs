namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A constraint on the shape of a chat response.
/// </summary>
public sealed class ResponseFormat
{
    private ResponseFormat(bool json, string schema)
    {
        IsJson = json;
        JsonSchema = schema;
    }

    /// <summary>The response must be a JSON value of any shape.</summary>
    public static ResponseFormat Json { get; } = new ResponseFormat(true, null);

    /// <summary>The response must conform to a JSON schema.</summary>
    /// <param name="jsonSchema">The schema, as JSON text.</param>
    /// <returns>The format.</returns>
    public static ResponseFormat FromJsonSchema(string jsonSchema) => new ResponseFormat(true, jsonSchema);

    /// <summary>Whether the response must be JSON.</summary>
    public bool IsJson { get; }

    /// <summary>The schema the response must conform to, or <see langword="null"/> for any JSON.</summary>
    public string JsonSchema { get; }
}
