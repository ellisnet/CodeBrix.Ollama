namespace ModelQueryTool.ChatTerminal.Editing;

/// <summary>
/// Distinguishes the three kinds of <see cref="InputToken"/>.
/// </summary>
public enum InputTokenKind
{
    /// <summary>A printable character to insert into the line.</summary>
    Character,

    /// <summary>A C0 control character (Enter, Backspace, Ctrl+C, …).</summary>
    Control,

    /// <summary>A decoded editing key (arrows, Home/End, Delete, paging).</summary>
    Key
}

/// <summary>
/// One decoded unit of terminal input: a printable character, a control
/// character, or an editing key decoded from a VT escape sequence.
/// Produced by <see cref="InputTokenizer"/>.
/// </summary>
public readonly struct InputToken
{
    private InputToken(InputTokenKind kind, char character, EditKey key)
    {
        Kind = kind;
        Character = character;
        Key = key;
    }

    /// <summary>The kind of token this is.</summary>
    public InputTokenKind Kind { get; }

    /// <summary>
    /// The character, when <see cref="Kind"/> is <see cref="InputTokenKind.Character"/>
    /// or <see cref="InputTokenKind.Control"/>.
    /// </summary>
    public char Character { get; }

    /// <summary>The editing key, when <see cref="Kind"/> is <see cref="InputTokenKind.Key"/>.</summary>
    public EditKey Key { get; }

    /// <summary>Creates a printable-character token.</summary>
    public static InputToken ForCharacter(char character) =>
        new(InputTokenKind.Character, character, default);

    /// <summary>Creates a control-character token.</summary>
    public static InputToken ForControl(char character) =>
        new(InputTokenKind.Control, character, default);

    /// <summary>Creates an editing-key token.</summary>
    public static InputToken ForKey(EditKey key) =>
        new(InputTokenKind.Key, default, key);
}
