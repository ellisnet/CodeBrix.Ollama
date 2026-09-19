namespace ModelQueryTool.ModelRunning;

/// <summary>
/// What the host loads the model with, and what it starts a conversation with. Nothing here is persisted:
/// an application that wants other values sets them before the host is used.
/// </summary>
public sealed class ModelHostOptions
{
    /// <summary>The smallest context size a reload will accept, below which a conversation has no room at all.</summary>
    public const uint MinimumContextSize = 512;

    /// <summary>
    /// Gets or sets the context size the model is loaded with. The key/value cache grows with it, so this is
    /// the dial between a conversation that has room and a model that will not load.
    /// </summary>
    public uint ContextSize { get; set; } = 8192;

    /// <summary>
    /// Gets or sets whether the processor back end may repack the weights into its faster layouts while
    /// loading. It is left off here: the repack buffer can approach the size of the model again, which is
    /// memory a machine running a model of this size does not have to spare.
    /// </summary>
    public bool UseExtraBufferTypes { get; set; }

    /// <summary>
    /// Gets or sets how many threads the arithmetic runs on, or null to let the model runner read the
    /// processor and choose.
    /// </summary>
    public int? Threads { get; set; }

    /// <summary>Gets or sets whether the model is asked to reason before it answers. On by default.</summary>
    public bool Think { get; set; } = true;

    /// <summary>
    /// Gets or sets the instruction that opens every conversation, or null for none - which is the default.
    /// </summary>
    public string SystemPrompt { get; set; }
}
