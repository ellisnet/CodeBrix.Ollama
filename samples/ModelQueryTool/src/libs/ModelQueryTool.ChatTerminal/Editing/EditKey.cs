namespace ModelQueryTool.ChatTerminal.Editing;

/// <summary>
/// The non-character editing keys the shell understands, decoded from the
/// VT escape sequences a terminal view sends (arrow keys, Home/End, Delete,
/// paging keys).
/// </summary>
public enum EditKey
{
    /// <summary>Cursor up — previous history entry.</summary>
    Up,

    /// <summary>Cursor down — next history entry.</summary>
    Down,

    /// <summary>Cursor left.</summary>
    Left,

    /// <summary>Cursor right.</summary>
    Right,

    /// <summary>Start of line.</summary>
    Home,

    /// <summary>End of line.</summary>
    End,

    /// <summary>Delete the character under the cursor.</summary>
    Delete,

    /// <summary>Page up (currently ignored by the line editor).</summary>
    PageUp,

    /// <summary>Page down (currently ignored by the line editor).</summary>
    PageDown
}
