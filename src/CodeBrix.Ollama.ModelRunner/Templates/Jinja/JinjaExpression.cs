namespace CodeBrix.Ollama.ModelRunner;

/// <summary>The base class of every Jinja expression node.</summary>
internal abstract class JinjaExpression : JinjaNode
{
    /// <summary>
    /// How tall the tree under this node is, itself included: 1 for a name or a literal. The parser sets
    /// it as it builds the tree, so that a tree too tall to render inside the stack is refused at parse.
    /// </summary>
    internal int Height { get; set; } = 1;
}
