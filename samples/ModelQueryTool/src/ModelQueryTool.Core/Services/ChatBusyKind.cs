namespace ModelQueryTool.Services;

/// <summary>
/// The long piece of work the chat is in the middle of, which is what it answers a line with
/// while it cannot answer it properly.
/// </summary>
internal enum ChatBusyKind
{
    /// <summary>Nothing long is running.</summary>
    None = 0,

    /// <summary>The model's files are being fetched.</summary>
    Downloading = 1,

    /// <summary>The weights are being read into memory, at start-up or at a new context size.</summary>
    Loading = 2,
}
