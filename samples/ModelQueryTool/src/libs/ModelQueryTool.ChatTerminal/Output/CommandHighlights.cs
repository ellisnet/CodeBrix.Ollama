using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ModelQueryTool.ChatTerminal.Commands;

namespace ModelQueryTool.ChatTerminal.Output;

/// <summary>
/// Finds the commands an application mentions in its own messages - "type /help for the commands",
/// "/think off turns it off" - so that they can be shown differently from the words around them.
/// It works in PLAIN TEXT and hands back offsets; what a mention is then drawn with belongs to
/// <see cref="TerminalText"/>.
/// </summary>
/// <remarks>
/// <para>
/// ONLY REGISTERED COMMANDS COUNT. A folder path (<c>/home/someone/.local/share</c>), a slash
/// inside a sentence (<c>and/or</c>) and a name nobody registered (<c>/nosuch</c>) are ordinary
/// text. That is the whole point of reading the registry rather than looking for a slash: the
/// application prints folder paths and quotes unknown names back at the user, and neither may be
/// made to look like something to type.
/// </para>
/// <para>
/// WHAT A MENTION IS. The prefix and a registered name, as a whole token: not preceded by a
/// letter, a digit, <c>_</c>, <c>/</c>, <c>.</c> or <c>-</c>, and not followed by a letter, a
/// digit, <c>_</c>, <c>/</c> or <c>-</c>. So <c>/home/someone</c>, <c>a/help</c>, <c>/helpful</c>
/// and <c>/think-tank</c> are not mentions, while <c>/help.</c>, <c>'/help'</c>, <c>(/help)</c>
/// and <c>/help,</c> are. Names are matched as they were registered, letter for letter, and the
/// longest name wins - <c>/set-system-prompt</c> is never found as <c>/set</c>.
/// </para>
/// <para>
/// THE ARGUMENTS COME WITH IT. What a person has to type is <c>/think off</c>, not <c>/think</c>,
/// so the span takes in the tokens written after the name for as long as each one is an option
/// (<c>-</c> and letters) or one of <c>on</c>, <c>off</c> and <c>on|off</c>. In "/think off turns
/// it off." the second "off" is a word of the sentence and stays outside the span.
/// </para>
/// <para>
/// THE TEXT HAS ALREADY BEEN WRAPPED. Everything this class is given has been laid out against the
/// terminal's width, so a row break can fall between a command and its argument - which is why a
/// gap inside a mention may be CR+LF as well as spaces.
/// </para>
/// <para>
/// THE REGISTRY IS READ LATE. An application registers its commands after it has built the pieces
/// that write text, so the names are taken when a message is looked at rather than when this class
/// was created, and the pattern is built again whenever the set of names has changed. Members are
/// safe to call from several threads at once.
/// </para>
/// </remarks>
public sealed class CommandHighlights
{
    /// <summary>What may not follow a name or an argument, so that a longer word is never a mention.</summary>
    private const string TokenEnd = @"(?![\p{L}\p{N}_/\-])";

    /// <summary>What may not come in front of the prefix, so that a path is never a mention.</summary>
    private const string TokenStart = @"(?<![\p{L}\p{N}_/.\-])";

    /// <summary>The space - or the row break the word wrapper left - between a name and an argument.</summary>
    private const string Gap = @"(?:[ \t]+|\r\n)+";

    /// <summary>An argument that belongs to the command in front of it.</summary>
    private const string ArgumentToken = @"(?:-\p{L}+|on\|off|off|on)";

    private static readonly CommandSpan[] NoSpans = [];

    private readonly CommandRegistry _registry;
    private readonly object _gate = new();

    private string[] _names;
    private Regex _pattern;

    /// <summary>Creates the highlights over the commands an application has registered.</summary>
    /// <param name="registry">The commands to look for; read again whenever the set of names changes.</param>
    /// <param name="prefix">
    /// What the user types in front of a name; a blank prefix takes
    /// <see cref="ChatLineInterpreter.CommandPrefix"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
    public CommandHighlights(CommandRegistry registry, string prefix = ChatLineInterpreter.CommandPrefix)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Prefix = string.IsNullOrEmpty(prefix) ? ChatLineInterpreter.CommandPrefix : prefix;
    }

    /// <summary>
    /// Creates the highlights over a fixed set of names, for a caller that has no registry - a
    /// test, or a piece of text written before anything was registered.
    /// </summary>
    /// <param name="names">The command names, WITHOUT the prefix; blank entries are ignored.</param>
    /// <param name="prefix">
    /// What the user types in front of a name; a blank prefix takes
    /// <see cref="ChatLineInterpreter.CommandPrefix"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is null.</exception>
    public CommandHighlights(IEnumerable<string> names, string prefix = ChatLineInterpreter.CommandPrefix)
    {
        if (names == null) { throw new ArgumentNullException(nameof(names)); }

        Prefix = string.IsNullOrEmpty(prefix) ? ChatLineInterpreter.CommandPrefix : prefix;
        _names = Sort(names);
        _pattern = Build(_names);
    }

    /// <summary>Gets what the user types in front of a command's name.</summary>
    public string Prefix { get; }

    /// <summary>
    /// Finds every mention of a registered command in a piece of plain text, in the order they
    /// appear and never overlapping.
    /// </summary>
    /// <param name="text">The text to look through; it must carry no escape sequences of its own.</param>
    /// <returns>
    /// One span per mention - the name and the arguments written with it - or none when there is
    /// nothing to find, nothing registered, or no text.
    /// </returns>
    public IReadOnlyList<CommandSpan> FindSpans(string text)
    {
        if (string.IsNullOrEmpty(text)) { return NoSpans; }

        var pattern = CurrentPattern();
        if (pattern == null) { return NoSpans; }

        List<CommandSpan> spans = null;

        foreach (Match match in pattern.Matches(text))
        {
            spans ??= [];
            spans.Add(new CommandSpan(match.Index, match.Length));
        }

        return spans ?? (IReadOnlyList<CommandSpan>)NoSpans;
    }

    /// <summary>
    /// The pattern for the names as they are now, built again when a command has been registered
    /// since the last look.
    /// </summary>
    private Regex CurrentPattern()
    {
        lock (_gate)
        {
            //Nothing to look at again: the names this one was given are the names it has
            if (_registry == null) { return _pattern; }

            var names = Sort(_registry.All.Select(command => command.Name));

            if (_names == null || !names.SequenceEqual(_names, StringComparer.Ordinal))
            {
                _names = names;
                _pattern = Build(names);
            }

            return _pattern;
        }
    }

    /// <summary>
    /// The names worth looking for, longest first so that a name which begins with another one is
    /// found whole: the alternation takes the first branch that fits.
    /// </summary>
    private static string[] Sort(IEnumerable<string> names) =>
        names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(name => name.Length)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Builds the pattern, or nothing at all when there are no names to look for.</summary>
    private Regex Build(string[] names)
    {
        if (names.Length == 0) { return null; }

        var pattern = new StringBuilder(TokenStart);

        pattern.Append(Regex.Escape(Prefix));
        pattern.Append("(?:");

        for (var index = 0; index < names.Length; index++)
        {
            if (index > 0) { pattern.Append('|'); }

            pattern.Append(Regex.Escape(names[index]));
        }

        pattern.Append(')');
        pattern.Append(TokenEnd);

        //The arguments a command is written with, for as long as they keep coming
        pattern.Append("(?:").Append(Gap).Append(ArgumentToken).Append(TokenEnd).Append(")*");

        //No IgnoreCase: a name is matched exactly as it was registered
        return new Regex(pattern.ToString());
    }
}
