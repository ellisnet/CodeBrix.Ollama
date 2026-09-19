using System.Text;

namespace ModelQueryTool.ChatTerminal.Output;

/// <summary>
/// The few lines an application writes into the terminal in its own voice rather than in the
/// model's: the prompt, a dimmed remark, a failure, a cleared screen. The escape sequences are
/// private to this class and it hands out finished strings, so nothing above it ever has to
/// know how a terminal is told to colour something.
/// </summary>
/// <remarks>
/// <para>
/// Every line this class produces ends its colour again, so whatever is written next inherits
/// nothing; and every line ends with CR+LF, because the terminal wants both and the session's
/// output surface emits line endings explicitly.
/// </para>
/// <para>
/// An empty message produces no bytes at all. A wrapper around an empty string would leave a
/// blank coloured row in the scrollback that the caller could not see coming.
/// </para>
/// <para>
/// <see cref="DimOn"/> and <see cref="DimOff"/> are the one pair meant to be written around
/// text that arrives a piece at a time - a model's reasoning, streamed through a
/// <see cref="StreamingWordWrapper"/> - which cannot be composed into a finished line in
/// advance.
/// </para>
/// <para>
/// THE COMMANDS AN APPLICATION MENTIONS stand out from the rest of its line: every message
/// method has an overload that takes a <see cref="CommandHighlights"/>, which says where in the
/// plain text a command was named. Such a mention is written as a reset, the highlight, the
/// mention, a reset, and then the line's own style again - so what follows carries on as it was
/// and the line still ends with exactly one reset. Passing no highlights, or highlights that
/// find nothing, produces exactly the bytes the one-argument methods produce.
/// </para>
/// <para>
/// A caller hands these methods text that has ALREADY been laid out against the terminal's
/// width. Wrapping first and styling afterwards is the only order that works: a word wrapper
/// counting escape sequences as characters would break its rows in the wrong places.
/// </para>
/// </remarks>
public static class TerminalText
{
    private const string Dim = "\x1b[2m";
    private const string Red = "\x1b[31m";
    private const string BoldCyan = "\x1b[1;36m";
    private const string BoldYellow = "\x1b[1;33m";
    private const string Reset = "\x1b[0m";
    private const string NewLine = "\r\n";
    private const string EraseScreenAndHome = "\x1b[2J\x1b[H";
    private const string PromptTail = "> ";

    /// <summary>
    /// Gets the sequence that starts dimmed text, for output that is written a piece at a time
    /// and so cannot be wrapped up as one finished line.
    /// </summary>
    public static string DimOn => Dim;

    /// <summary>
    /// Gets the sequence that ends dimmed text. It resets every attribute rather than only the
    /// dim one, so nothing written afterwards can inherit a half-set style.
    /// </summary>
    public static string DimOff => Reset;

    /// <summary>
    /// Dims a remark of the application's own - what it is doing, what it found, what a command
    /// would do - WITHOUT ending the line, for a caller that supplies its own line ending.
    /// </summary>
    /// <param name="message">The remark to show.</param>
    /// <returns>The dimmed remark, or an empty string when there is no remark to show.</returns>
    public static string Dimmed(string message) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : Dim + message + Reset;

    /// <summary>
    /// Dims a remark of the application's own WITHOUT ending the line, showing the commands it
    /// names in the highlight colour.
    /// </summary>
    /// <param name="message">The remark to show, as plain text that has already been wrapped.</param>
    /// <param name="commands">Which commands to pick out; null highlights none.</param>
    /// <returns>The dimmed remark, or an empty string when there is no remark to show.</returns>
    public static string Dimmed(string message, CommandHighlights commands) =>
        Compose(message, commands, Dim);

    /// <summary>Wraps a remark of the application's own as a dimmed line of its own.</summary>
    /// <param name="message">The remark to show.</param>
    /// <returns>
    /// The remark with the escape sequences and the line ending a terminal needs, or an empty
    /// string when there is no remark to show.
    /// </returns>
    public static string Notice(string message) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : Dimmed(message) + NewLine;

    /// <summary>
    /// Wraps a remark as a dimmed line of its own, showing the commands it names in the highlight
    /// colour.
    /// </summary>
    /// <param name="message">The remark to show, as plain text that has already been wrapped.</param>
    /// <param name="commands">Which commands to pick out; null highlights none.</param>
    /// <returns>
    /// The remark with the escape sequences and the line ending a terminal needs, or an empty
    /// string when there is no remark to show.
    /// </returns>
    public static string Notice(string message, CommandHighlights commands) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : Dimmed(message, commands) + NewLine;

    /// <summary>
    /// Colours a failure red WITHOUT ending the line, for a caller that supplies its own line
    /// ending.
    /// </summary>
    /// <param name="message">The failure to show, written for a person and never a stack trace.</param>
    /// <returns>The red failure, or an empty string when there is no failure to show.</returns>
    public static string Failure(string message) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : Red + message + Reset;

    /// <summary>
    /// Colours a failure red WITHOUT ending the line, showing the commands it names in the
    /// highlight colour.
    /// </summary>
    /// <param name="message">The failure to show, as plain text that has already been wrapped.</param>
    /// <param name="commands">Which commands to pick out; null highlights none.</param>
    /// <returns>The red failure, or an empty string when there is no failure to show.</returns>
    public static string Failure(string message, CommandHighlights commands) =>
        Compose(message, commands, Red);

    /// <summary>Wraps a failure as a red line of its own.</summary>
    /// <param name="message">The failure to show, written for a person and never a stack trace.</param>
    /// <returns>
    /// The failure with the escape sequences and the line ending a terminal needs, or an empty
    /// string when there is no failure to show.
    /// </returns>
    public static string Error(string message) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : Failure(message) + NewLine;

    /// <summary>
    /// Wraps a failure as a red line of its own, showing the commands it names in the highlight
    /// colour.
    /// </summary>
    /// <param name="message">The failure to show, as plain text that has already been wrapped.</param>
    /// <param name="commands">Which commands to pick out; null highlights none.</param>
    /// <returns>
    /// The failure with the escape sequences and the line ending a terminal needs, or an empty
    /// string when there is no failure to show.
    /// </returns>
    public static string Error(string message, CommandHighlights commands) =>
        string.IsNullOrEmpty(message)
            ? string.Empty
            : Failure(message, commands) + NewLine;

    /// <summary>
    /// Shows the commands a line names in the highlight colour and leaves the rest of it exactly
    /// as it was - for the lines an application writes in no style of their own, such as a help
    /// listing or a report.
    /// </summary>
    /// <param name="message">The line, as plain text that has already been wrapped.</param>
    /// <param name="commands">Which commands to pick out; null highlights none.</param>
    /// <returns>
    /// The line with its commands highlighted, or the line exactly as it was when there is
    /// nothing to highlight in it.
    /// </returns>
    /// <remarks>This one carries NO line ending: the caller's own <c>WriteLine</c> supplies it.</remarks>
    public static string Plain(string message, CommandHighlights commands) =>
        Compose(message, commands, string.Empty);

    /// <summary>
    /// Shows one command in the highlight colour, for a caller that composes a line itself and
    /// knows where the command is.
    /// </summary>
    /// <param name="text">The command as the user would type it, arguments and all.</param>
    /// <returns>The highlighted command, or an empty string when there is nothing to show.</returns>
    public static string Command(string text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : BoldYellow + text + Reset;

    /// <summary>
    /// Builds the prompt: the label in bold cyan, then the plain angle bracket and the space
    /// the user's own text starts after.
    /// </summary>
    /// <param name="label">The word in front of the bracket; blank leaves the bracket alone.</param>
    /// <returns>The prompt string, which is what a session is given as its prompt.</returns>
    /// <remarks>
    /// The colour costs no cells: the session measures a prompt with
    /// <see cref="Editing.CellWidth.OfText"/>, which counts an escape sequence as nothing.
    /// </remarks>
    public static string Prompt(string label) =>
        string.IsNullOrWhiteSpace(label)
            ? PromptTail
            : BoldCyan + label + Reset + PromptTail;

    /// <summary>Erases the whole screen and puts the cursor back in its top left cell.</summary>
    /// <returns>The escape sequences that clear a terminal.</returns>
    public static string ClearScreen() => EraseScreenAndHome;

    /// <summary>
    /// Writes one line in the style it belongs to, with every command it names in the highlight
    /// colour and the style put back on afterwards.
    /// </summary>
    /// <param name="message">The line, as plain text.</param>
    /// <param name="commands">Which commands to pick out; null highlights none.</param>
    /// <param name="style">The line's own style, or an empty string for a line with none.</param>
    /// <returns>The finished line, without a line ending.</returns>
    /// <remarks>
    /// With nothing to highlight this is the one-argument methods' own expression, byte for byte.
    /// The style is only put back on when there is more line to write, so a mention at the end of
    /// a line closes it with one reset rather than with a style nobody uses.
    /// </remarks>
    private static string Compose(string message, CommandHighlights commands, string style)
    {
        if (string.IsNullOrEmpty(message)) { return string.Empty; }

        var spans = commands?.FindSpans(message);
        if (spans == null || spans.Count == 0)
        {
            return style.Length == 0 ? message : style + message + Reset;
        }

        var line = new StringBuilder(style);
        var written = 0;
        var styleIsOn = style.Length > 0;

        foreach (var span in spans)
        {
            line.Append(message, written, span.Start - written);
            line.Append(Reset).Append(BoldYellow).Append(message, span.Start, span.Length).Append(Reset);
            written = span.End;

            styleIsOn = style.Length > 0 && written < message.Length;
            if (styleIsOn) { line.Append(style); }
        }

        line.Append(message, written, message.Length - written);

        if (styleIsOn) { line.Append(Reset); }

        return line.ToString();
    }
}
