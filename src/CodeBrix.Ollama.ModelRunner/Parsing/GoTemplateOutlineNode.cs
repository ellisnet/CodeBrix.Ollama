using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go text/template/parse (node shapes, reference only);

/// <summary>
/// One node in the structural outline of a Go text/template produced by <see cref="GoTemplateOutline"/>.
/// </summary>
/// <remarks>
/// The outline keeps only what Ollama's two template heuristics need: the literal text nodes, the nesting
/// of <c>if</c>, <c>range</c> and <c>with</c> blocks, and the field references (<c>.Messages</c>,
/// <c>.Thinking</c>, <c>.ToolCalls</c>) each pipeline mentions. It is deliberately not a template engine.
/// </remarks>
internal sealed class GoTemplateOutlineNode
{
    /// <summary>The kind of node.</summary>
    public GoTemplateOutlineKind Kind { get; set; }

    /// <summary>
    /// The literal text, for a <see cref="GoTemplateOutlineKind.Text"/> node. Trim markers have already
    /// been applied. Empty for every other kind.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The raw pipeline source of an action or block node, with the delimiters and any trim markers
    /// removed. Empty for a text node.
    /// </summary>
    public string Pipeline { get; set; } = string.Empty;

    /// <summary>
    /// The field references in this node's own pipeline, each split on its dots, so <c>.Function.Name</c>
    /// arrives as <c>["Function", "Name"]</c>. Variables such as <c>$.Messages</c> are not field
    /// references and are not listed, which is what Go's parser does too.
    /// </summary>
    public IList<IList<string>> PipelineFields { get; } = new List<IList<string>>();

    /// <summary>The body of a block node, in source order. Empty for text and action nodes.</summary>
    public IList<GoTemplateOutlineNode> Body { get; } = new List<GoTemplateOutlineNode>();

    /// <summary>
    /// The <c>{{ else }}</c> body of a block node, or <see langword="null"/> when the block has none. An
    /// <c>{{ else if }}</c> arrives as a single <see cref="GoTemplateOutlineKind.If"/> node here, exactly
    /// as Go's parser rewrites it.
    /// </summary>
    public IList<GoTemplateOutlineNode> ElseBody { get; set; }
}
