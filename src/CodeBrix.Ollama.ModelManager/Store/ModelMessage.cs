using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One preset conversation message from a model's MESSAGE layer.
/// </summary>
public sealed class ModelMessage
{
    /// <summary>
    /// Initializes an empty message for deserialization.
    /// </summary>
    public ModelMessage()
    {
    }

    /// <summary>
    /// Initializes a message.
    /// </summary>
    /// <param name="role">"system", "user" or "assistant".</param>
    /// <param name="content">The message text.</param>
    public ModelMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    /// <summary>"system", "user" or "assistant".</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; }

    /// <summary>The message text.</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; }
}
