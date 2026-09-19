using System;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.Output;

namespace ModelQueryTool.ChatTerminal;

/// <summary>
/// The root interpreter of a chat: the conversation is what a line IS, and a
/// command is the exception. A line whose first non-blank character is a slash
/// goes to the <see cref="CommandRegistry"/>; every other non-blank line is
/// handed VERBATIM to the application's chat handler, which streams the model's
/// answer to <see cref="ShellSession.Output"/>.
/// </summary>
/// <remarks>
/// <para>
/// Commands are registered WITHOUT the slash - "think", "set-system-prompt" -
/// and the slash is stripped before the lookup, so one registry serves a chat
/// and a command-only session alike. A slash that names nothing prints a single
/// line pointing at <see cref="CommandPrefix"/>help rather than a stack of
/// suggestions.
/// </para>
/// <para>
/// Nothing is trimmed, unescaped or otherwise touched on the way to the chat
/// handler: the quotes, the backslashes and the line breaks a multi-line paste
/// left in the line are all part of the question the user is asking.
/// </para>
/// <para>
/// The handler runs on the session's worker thread with the line's cancellation
/// token, which Ctrl+C signals - so cancelling a turn is the handler abandoning
/// its token, exactly as it is for a command.
/// </para>
/// </remarks>
public sealed class ChatLineInterpreter : ILineInterpreter
{
    /// <summary>The character that turns a line into a command.</summary>
    public const string CommandPrefix = "/";

    private readonly Func<ShellSession, string, CancellationToken, Task> _chatHandler;
    private readonly CommandHighlights _highlights;

    /// <summary>Creates the interpreter.</summary>
    /// <param name="commands">
    /// The commands slash-prefixed lines are dispatched to, registered under
    /// their bare names.
    /// </param>
    /// <param name="chatHandler">
    /// What the application does with an ordinary line: send it to the model and
    /// write the answer. It is given the session, the line verbatim, and the
    /// token Ctrl+C signals.
    /// </param>
    /// <param name="prompt">The prompt shown while this interpreter is active.</param>
    /// <param name="highlights">
    /// Which commands to show in the highlight colour in the interpreter's own
    /// one line of output; null writes that line in no colour at all.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="commands"/> or <paramref name="chatHandler"/> is null.
    /// </exception>
    public ChatLineInterpreter(CommandRegistry commands,
        Func<ShellSession, string, CancellationToken, Task> chatHandler,
        string prompt = "> ",
        CommandHighlights highlights = null)
    {
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _chatHandler = chatHandler ?? throw new ArgumentNullException(nameof(chatHandler));
        Prompt = prompt ?? string.Empty;
        _highlights = highlights;
    }

    /// <summary>The commands a slash-prefixed line is dispatched to.</summary>
    public CommandRegistry Commands { get; }

    /// <inheritdoc/>
    public string Prompt { get; }

    /// <summary>
    /// Whether this line is a command rather than something to say to the model:
    /// its first non-blank character is a slash.
    /// </summary>
    /// <param name="line">The submitted line.</param>
    /// <returns>True when the line names a command.</returns>
    public static bool IsCommand(string line) =>
        !string.IsNullOrWhiteSpace(line) && line.TrimStart().StartsWith(CommandPrefix, StringComparison.Ordinal);

    /// <inheritdoc/>
    public Task HandleLineAsync(ShellSession session, string line,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line)) { return Task.CompletedTask; }

        if (!IsCommand(line))
        {
            return _chatHandler(session, line, cancellationToken);
        }

        var commandLine = line.TrimStart().Substring(CommandPrefix.Length);

        //The name the user typed is NOT a command, so only the one that is - help - lights up
        return CommandInterpreter.DispatchAsync(Commands, session, commandLine,
            name => session.Output.WriteLine(TerminalText.Plain(
                $"There is no {CommandPrefix}{name} command - {CommandPrefix}help lists the ones there are.",
                _highlights)),
            cancellationToken);
    }
}
