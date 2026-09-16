using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama template/template.go;

/// <summary>
/// Everything an Ollama chat template can see while it renders: the conversation, the tools on offer,
/// and the fill-in-the-middle and thinking settings. It mirrors Ollama's <c>template.Values</c>.
/// </summary>
public sealed class OllamaTemplateValues
{
    /// <summary>
    /// The conversation, oldest first. Consecutive messages with the same role are merged before the
    /// template sees them, and the content of every system message is also offered separately as
    /// <c>.System</c>.
    /// </summary>
    public IList<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    /// <summary>The tools the model may call, which the template sees as <c>.Tools</c>.</summary>
    public IList<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();

    /// <summary>
    /// The text that precedes the insertion point in a fill-in-the-middle request. The template sees it
    /// as <c>.Prompt</c>, but only when <see cref="Suffix"/> is set as well. A template written in the
    /// older <c>.Prompt</c> / <c>.Response</c> form does not read this: it is rendered once per exchange
    /// from <see cref="Messages"/>, and its <c>.Prompt</c> is the user message of the exchange being
    /// rendered. May be <see langword="null"/>.
    /// </summary>
    public string Prompt { get; set; }

    /// <summary>
    /// The text that follows the insertion point in a fill-in-the-middle request. When both this and
    /// <see cref="Prompt"/> are set, the template renders in its suffix form and nothing else is passed.
    /// May be <see langword="null"/>.
    /// </summary>
    public string Suffix { get; set; }

    /// <summary>
    /// Instructions to prepend to the conversation as a system message, for callers that keep the system
    /// prompt out of <see cref="Messages"/>. May be <see langword="null"/>.
    /// </summary>
    public string System { get; set; }

    /// <summary>
    /// Whether the model is being asked to think before answering. <see langword="null"/> means the
    /// caller did not say, which the template sees as <c>.IsThinkSet</c> being false.
    /// </summary>
    public bool? Think { get; set; }

    /// <summary>
    /// How much the model should think, for models whose template offers levels such as "low" or "high".
    /// May be <see langword="null"/>.
    /// </summary>
    public string ThinkLevel { get; set; }

    /// <summary>
    /// Forces the older <c>.Prompt</c> / <c>.Response</c> rendering even for a template that ranges over
    /// <c>.Messages</c>. Ollama keeps this for its own compatibility tests and so do we.
    /// </summary>
    internal bool ForceLegacy { get; set; }
}
