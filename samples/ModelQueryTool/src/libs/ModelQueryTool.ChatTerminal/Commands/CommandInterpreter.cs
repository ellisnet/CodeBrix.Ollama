using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModelQueryTool.ChatTerminal.Commands;

/// <summary>
/// A line interpreter for which EVERY line is a command: it tokenizes the line,
/// looks the first token up in the <see cref="CommandRegistry"/>, and executes
/// it. A chat uses <see cref="ChatLineInterpreter"/> instead, where the root is
/// the conversation and only slash-prefixed lines come here.
/// </summary>
public sealed class CommandInterpreter : ILineInterpreter
{
    private readonly CommandRegistry _registry;

    /// <summary>Creates the interpreter over a registry, with the prompt it shows.</summary>
    /// <param name="registry">The commands to dispatch.</param>
    /// <param name="prompt">The prompt shown while this interpreter is active.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
    public CommandInterpreter(CommandRegistry registry, string prompt)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Prompt = prompt ?? string.Empty;
    }

    /// <inheritdoc/>
    public string Prompt { get; }

    /// <inheritdoc/>
    public Task HandleLineAsync(ShellSession session, string line,
        CancellationToken cancellationToken) =>
        DispatchAsync(_registry, session, line,
            name => session.Output.WriteLine($"Unknown command: {name}  (try 'help')"),
            cancellationToken);

    /// <summary>
    /// Runs one command line: the first word names the command, the rest are its
    /// arguments. Shared with <see cref="ChatLineInterpreter"/>, which strips the
    /// slash and hands the remainder here.
    /// </summary>
    /// <param name="registry">The commands to look in.</param>
    /// <param name="session">The session the command runs in.</param>
    /// <param name="commandLine">The line, WITHOUT any prefix the caller strips.</param>
    /// <param name="onUnknown">Called with the command word when no command has that name.</param>
    /// <param name="cancellationToken">Signalled when the user presses Ctrl+C.</param>
    internal static async Task DispatchAsync(CommandRegistry registry, ShellSession session,
        string commandLine, Action<string> onUnknown, CancellationToken cancellationToken)
    {
        var tokens = CommandLineTokenizer.Tokenize(commandLine);
        if (tokens.Count == 0) { return; }

        if (!registry.TryGet(tokens[0], out var command))
        {
            onUnknown(tokens[0]);
            return;
        }

        var arguments = tokens.GetRange(1, tokens.Count - 1);
        var context = new ShellCommandContext(
            session, arguments, RawArgumentsOf(commandLine), cancellationToken);
        await command.ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the line with its leading whitespace and its command word removed, and
    /// nothing else changed — <see cref="ShellCommandContext.RawArguments"/>.
    /// </summary>
    /// <param name="line">The line as typed.</param>
    /// <returns>The verbatim remainder of the line.</returns>
    /// <remarks>
    /// The command word is taken as the first run of non-whitespace, NOT looked up in
    /// the token list: a token may differ from the text it was read from (the tokenizer
    /// drops quotes), so searching for it would fail on text this has no trouble with.
    /// Every registered command name is a plain word, so the two agree wherever a
    /// command was actually found.
    /// </remarks>
    private static string RawArgumentsOf(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index])) { index++; }
        while (index < line.Length && !char.IsWhiteSpace(line[index])) { index++; }

        return line.Substring(index).Trim();
    }
}