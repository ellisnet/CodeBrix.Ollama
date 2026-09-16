using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A chat request: the conversation so far, the tools on offer and how to generate the reply.
/// </summary>
public sealed class ChatRequest
{
    /// <summary>The conversation, oldest first. The library renders it through the model's chat template.</summary>
    public IList<ChatMessage> Messages { get; } = new List<ChatMessage>();

    /// <summary>The tools the model may call. Empty by default.</summary>
    public IList<ToolDefinition> Tools { get; } = new List<ToolDefinition>();

    /// <summary>
    /// Whether the model should think before answering, for models whose template has a switch for it.
    /// <see langword="null"/> (the default) leaves the template's default; the value is ignored by templates
    /// without the switch.
    /// </summary>
    public bool? Think { get; set; }

    /// <summary>A constraint on the shape of the reply. <see langword="null"/> (the default) means free text.</summary>
    public ResponseFormat ResponseFormat { get; set; }

    /// <summary>The generation controls. <see langword="null"/> (the default) uses <see cref="GenerationOptions"/>' defaults.</summary>
    public GenerationOptions Options { get; set; }
}
