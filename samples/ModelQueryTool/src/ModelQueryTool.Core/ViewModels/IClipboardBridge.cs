using System;

namespace ModelQueryTool.ViewModels;

/// <summary>
/// The system clipboard, which only a head can reach. The page fills this in when it has one;
/// a head that has none - the frame buffer head has no desktop clipboard - leaves it alone, and
/// the view model then answers every copy with "it could not be done" rather than pretending.
/// </summary>
public interface IClipboardBridge
{
    /// <summary>
    /// Gets or sets the head's own copy: it is handed the text verbatim, runs on the thread that
    /// owns the user interface, and answers whether the text reached the clipboard.
    /// </summary>
    Func<string, bool> CopyTextToClipboard { get; set; }
}
