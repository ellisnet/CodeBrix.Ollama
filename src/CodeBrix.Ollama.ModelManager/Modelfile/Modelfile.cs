using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama parser/parser.go and api/types.go;

/// <summary>
/// A parsed Modelfile: the ordered list of commands Ollama's parser produces, plus typed views over
/// them. Anything Ollama's parser accepts, this accepts, and the error text and line numbers match.
/// </summary>
/// <remarks>
/// This is the parser only. Turning a FROM or ADAPTER argument into files on disk - globbing,
/// hashing, detecting safetensors, expanding "~" - belongs to the model store, which resolves paths
/// later.
/// </remarks>
public sealed class Modelfile
{
    private const string ErrMissingFrom = "no FROM line";

    private const string ErrUnexpectedEof = "unexpected EOF";

    private const string ErrInvalidMessageRole =
        "message role must be one of \"system\", \"user\", or \"assistant\"";

    private const string ErrInvalidCommand =
        "command must be one of \"from\", \"license\", \"template\", \"system\", \"adapter\", " +
        "\"draft\", \"renderer\", \"parser\", \"parameter\", \"message\", or \"requires\"";

    private static readonly string[] DeprecatedParameterNames =
    {
        "penalize_newline",
        "low_vram",
        "f16_kv",
        "logits_all",
        "vocab_only",
        "use_mlock",
        "mirostat",
        "mirostat_tau",
        "mirostat_eta",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="Modelfile"/> class from a list of commands. The
    /// typed views are computed here; the commands themselves are not validated, so a list without
    /// a FROM command is allowed (only <see cref="Parse(string)"/> insists on one).
    /// </summary>
    /// <param name="commands">The commands, in file order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="commands"/> contains a null command.</exception>
    public Modelfile(IEnumerable<ModelfileCommand> commands)
    {
        if (commands == null)
        {
            throw new ArgumentNullException(nameof(commands));
        }

        var all = new List<ModelfileCommand>();
        var modelArgs = new List<string>();
        var adapters = new List<string>();
        var drafts = new List<string>();
        var licenses = new List<string>();
        var messages = new List<ModelMessage>();
        var parameterLines = new List<KeyValuePair<string, string>>();
        var deprecated = new List<string>();

        foreach (var command in commands)
        {
            if (command == null)
            {
                throw new ArgumentException("A Modelfile command cannot be null.", nameof(commands));
            }

            all.Add(command);

            switch (command.Name)
            {
                case "model":
                    modelArgs.Add(command.Args);
                    break;
                case "adapter":
                    adapters.Add(command.Args);
                    break;
                case "draft":
                    drafts.Add(command.Args);
                    break;
                case "license":
                    licenses.Add(command.Args);
                    break;
                case "template":
                    Template = command.Args;
                    break;
                case "system":
                    System = command.Args;
                    break;
                case "renderer":
                    Renderer = command.Args;
                    break;
                case "parser":
                    Parser = command.Args;
                    break;
                case "requires":
                    Requires = command.Args;
                    break;
                case "message":
                    messages.Add(ToMessage(command.Args));
                    break;
                default:
                    parameterLines.Add(new KeyValuePair<string, string>(command.Name, command.Args));
                    if (IsDeprecatedParameter(command.Name))
                    {
                        deprecated.Add(command.Name);
                    }

                    break;
            }
        }

        Commands = new ReadOnlyCollection<ModelfileCommand>(all);
        ModelArgs = new ReadOnlyCollection<string>(modelArgs);
        From = modelArgs.Count > 0 ? modelArgs[0] : null;
        Adapters = new ReadOnlyCollection<string>(adapters);
        Drafts = new ReadOnlyCollection<string>(drafts);
        Licenses = new ReadOnlyCollection<string>(licenses);
        Messages = new ReadOnlyCollection<ModelMessage>(messages);
        ParameterLines = new ReadOnlyCollection<KeyValuePair<string, string>>(parameterLines);
        DeprecatedParameters = new ReadOnlyCollection<string>(deprecated);
    }

    /// <summary>
    /// Every command in the file, in order.
    /// </summary>
    public IReadOnlyList<ModelfileCommand> Commands { get; }

    /// <summary>
    /// The base model: the argument of the first FROM command, or null when the file has none.
    /// </summary>
    public string From { get; }

    /// <summary>
    /// The argument of every FROM command, in order. The first is the base model; Ollama treats any
    /// further ones as extra files for the same model, such as a projector.
    /// </summary>
    public IReadOnlyList<string> ModelArgs { get; }

    /// <summary>
    /// The argument of the last TEMPLATE command, or null when the file has none.
    /// </summary>
    public string Template { get; }

    /// <summary>
    /// The argument of the last SYSTEM command, or null when the file has none.
    /// </summary>
    public string System { get; }

    /// <summary>
    /// The argument of every ADAPTER command, in order.
    /// </summary>
    public IReadOnlyList<string> Adapters { get; }

    /// <summary>
    /// The argument of every DRAFT command, in order.
    /// </summary>
    public IReadOnlyList<string> Drafts { get; }

    /// <summary>
    /// The argument of every LICENSE command, in order.
    /// </summary>
    public IReadOnlyList<string> Licenses { get; }

    /// <summary>
    /// The argument of the last RENDERER command, or null when the file has none.
    /// </summary>
    public string Renderer { get; }

    /// <summary>
    /// The argument of the last PARSER command, or null when the file has none.
    /// </summary>
    public string Parser { get; }

    /// <summary>
    /// The argument of the last REQUIRES command, exactly as written, or null when the file has
    /// none. Ollama checks this against its own version when the model is created; the value is not
    /// validated here.
    /// </summary>
    public string Requires { get; }

    /// <summary>
    /// Every MESSAGE command as a role and content pair, in order.
    /// </summary>
    public IReadOnlyList<ModelMessage> Messages { get; }

    /// <summary>
    /// Every PARAMETER command as a name and raw value pair, in order, including repeated names and
    /// deprecated ones. <see cref="GetParameters"/> turns these into typed values.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ParameterLines { get; }

    /// <summary>
    /// The name of every PARAMETER command Ollama no longer honours, in order. These are dropped by
    /// <see cref="GetParameters"/> rather than reported as unknown.
    /// </summary>
    public IReadOnlyList<string> DeprecatedParameters { get; }

    /// <summary>
    /// Parses Modelfile text.
    /// </summary>
    /// <param name="text">The whole file as text. A leading byte order mark is ignored.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ModelfileParseException">The text is not a valid Modelfile.</exception>
    public static Modelfile Parse(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return new Modelfile(ParseCommands(text));
    }

    /// <summary>
    /// Parses Modelfile text read from a reader.
    /// </summary>
    /// <param name="reader">The reader, read to the end.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    /// <exception cref="ModelfileParseException">The text is not a valid Modelfile.</exception>
    public static Modelfile Parse(TextReader reader)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Reads and parses a Modelfile from disk. UTF-8 is assumed; a UTF-8, UTF-16 little-endian or
    /// UTF-16 big-endian byte order mark is honoured, as it is by Ollama.
    /// </summary>
    /// <param name="path">The path of the file to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ModelfileParseException">The text is not a valid Modelfile.</exception>
    public static async Task<Modelfile> ReadFileAsync(string path,
        CancellationToken cancellationToken = default)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        string text;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        return Parse(text);
    }

    /// <summary>
    /// Renders the whole file: every command's line, each followed by a newline.
    /// </summary>
    /// <returns>The Modelfile text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var command in Commands)
        {
            builder.Append(command.ToString());
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Applies Ollama's parameter typing to <see cref="ParameterLines"/>. Deprecated parameters are
    /// dropped (they are listed in <see cref="DeprecatedParameters"/>); repeated "stop" values are
    /// collected in order; every other repeated name keeps its last value.
    /// </summary>
    /// <returns>The typed parameters.</returns>
    /// <exception cref="ModelfileParseException">A parameter name is not one Ollama knows, or a
    /// value cannot be read as the type that parameter takes.</exception>
    public ModelParameters GetParameters()
    {
        var parameters = new ModelParameters();

        foreach (var line in ParameterLines)
        {
            var name = line.Key;
            var value = line.Value;

            if (IsDeprecatedParameter(name))
            {
                continue;
            }

            switch (name)
            {
                case "num_keep":
                    parameters.NumKeep = ParseInt(name, value);
                    break;
                case "seed":
                    parameters.Seed = ParseInt(name, value);
                    break;
                case "num_predict":
                    parameters.NumPredict = ParseInt(name, value);
                    break;
                case "top_k":
                    parameters.TopK = ParseInt(name, value);
                    break;
                case "top_p":
                    parameters.TopP = ParseFloat(value);
                    break;
                case "min_p":
                    parameters.MinP = ParseFloat(value);
                    break;
                case "typical_p":
                    parameters.TypicalP = ParseFloat(value);
                    break;
                case "repeat_last_n":
                    parameters.RepeatLastN = ParseInt(name, value);
                    break;
                case "temperature":
                    parameters.Temperature = ParseFloat(value);
                    break;
                case "repeat_penalty":
                    parameters.RepeatPenalty = ParseFloat(value);
                    break;
                case "presence_penalty":
                    parameters.PresencePenalty = ParseFloat(value);
                    break;
                case "frequency_penalty":
                    parameters.FrequencyPenalty = ParseFloat(value);
                    break;
                case "stop":
                    parameters.Stop ??= new List<string>();
                    parameters.Stop.Add(value);
                    break;
                case "num_ctx":
                    parameters.NumCtx = ParseInt(name, value);
                    break;
                case "num_batch":
                    parameters.NumBatch = ParseInt(name, value);
                    break;
                case "num_gpu":
                    parameters.NumGpu = ParseInt(name, value);
                    break;
                case "main_gpu":
                    parameters.MainGpu = ParseInt(name, value);
                    break;
                case "use_mmap":
                    parameters.UseMmap = ParseBool(value);
                    break;
                case "num_thread":
                    parameters.NumThread = ParseInt(name, value);
                    break;
                case "draft_num_predict":
                    parameters.DraftNumPredict = ParseInt(name, value);
                    break;
                default:
                    throw new ModelfileParseException(0, $"unknown parameter '{name}'");
            }
        }

        return parameters;
    }

    /// <summary>
    /// Quotes an argument the way Ollama does when it writes a Modelfile line: text holding a
    /// newline, or starting or ending with a space, is wrapped in three double quotes when it also
    /// holds a double quote, and in one otherwise. Anything else is returned unchanged.
    /// </summary>
    /// <param name="value">The argument text.</param>
    /// <returns>The quoted text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static string Quote(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Contains('\n')
            || value.StartsWith(" ", StringComparison.Ordinal)
            || value.EndsWith(" ", StringComparison.Ordinal))
        {
            if (value.Contains('"'))
            {
                return "\"\"\"" + value + "\"\"\"";
            }

            return "\"" + value + "\"";
        }

        return value;
    }

    /// <summary>
    /// Removes the quoting <see cref="Quote"/> adds. Text wrapped in three double quotes, or in
    /// one, loses them; anything else is returned unchanged. Text that opens a quote without
    /// closing it is rejected, which is how the parser knows a value continues on the next line.
    /// Single quotes are not quoting characters, matching Ollama (its source carries an open
    /// "TODO: single quotes" note).
    /// </summary>
    /// <param name="value">The argument text, already trimmed.</param>
    /// <param name="result">The unquoted text, or an empty string when the quoting is unclosed.</param>
    /// <returns><see langword="true"/> when the text was unquoted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static bool TryUnquote(string value, out string result)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Length >= 3 && value.StartsWith("\"\"\"", StringComparison.Ordinal))
        {
            if (value.Length >= 6 && value.EndsWith("\"\"\"", StringComparison.Ordinal))
            {
                result = value.Substring(3, value.Length - 6);
                return true;
            }

            result = string.Empty;
            return false;
        }

        if (value.Length >= 1 && value[0] == '"')
        {
            if (value.Length >= 2 && value[value.Length - 1] == '"')
            {
                result = value.Substring(1, value.Length - 2);
                return true;
            }

            result = string.Empty;
            return false;
        }

        result = value;
        return true;
    }

    private static List<ModelfileCommand> ParseCommands(string text)
    {
        var commands = new List<ModelfileCommand>();
        var state = ModelfileParserState.Nil;
        var currentLine = 1;
        var buffer = new StringBuilder();
        var role = string.Empty;
        string commandName = null;

        foreach (var rune in StripByteOrderMark(text).EnumerateRunes())
        {
            if (IsNewline(rune))
            {
                currentLine++;
            }

            var next = ParseRuneForState(rune, state, currentLine, buffer, out var current);

            // Process the state transition; some transitions are intercepted and redirected.
            if (next != state)
            {
                switch (state)
                {
                    case ModelfileParserState.Name:
                    {
                        var keyword = buffer.ToString();
                        if (!IsValidCommand(keyword))
                        {
                            throw new ModelfileParseException(currentLine, ErrInvalidCommand);
                        }

                        // The next state sometimes depends on the buffered keyword.
                        keyword = keyword.ToLowerInvariant();
                        if (keyword == "from")
                        {
                            commandName = "model";
                        }
                        else if (keyword == "parameter")
                        {
                            // Move to Parameter, which sets the command name from the parameter name.
                            next = ModelfileParserState.Parameter;
                        }
                        else
                        {
                            if (keyword == "message")
                            {
                                // Move to Message, which validates the role.
                                next = ModelfileParserState.Message;
                            }

                            commandName = keyword;
                        }

                        break;
                    }

                    case ModelfileParserState.Parameter:
                        commandName = buffer.ToString();
                        break;

                    case ModelfileParserState.Message:
                    {
                        var messageRole = buffer.ToString();
                        if (!IsValidMessageRole(messageRole))
                        {
                            throw new ModelfileParseException(currentLine, ErrInvalidMessageRole);
                        }

                        role = messageRole;
                        break;
                    }

                    case ModelfileParserState.Comment:
                    case ModelfileParserState.Nil:
                        break;

                    case ModelfileParserState.Value:
                    {
                        if (!TryUnquote(buffer.ToString().Trim(), out var args) || IsSpace(current))
                        {
                            // The value is not finished: keep the character and keep reading.
                            buffer.Append(current.ToString());
                            continue;
                        }

                        if (role.Length > 0)
                        {
                            args = role + ": " + args;
                            role = string.Empty;
                        }

                        commands.Add(new ModelfileCommand(commandName, args));
                        break;
                    }
                }

                buffer.Clear();
                state = next;
            }

            if (IsPrint(current))
            {
                buffer.Append(current.ToString());
            }
        }

        // Flush the buffer.
        switch (state)
        {
            case ModelfileParserState.Comment:
            case ModelfileParserState.Nil:
                break;
            case ModelfileParserState.Value:
            {
                if (!TryUnquote(buffer.ToString().Trim(), out var args))
                {
                    throw new ModelfileParseException(0, ErrUnexpectedEof);
                }

                if (role.Length > 0)
                {
                    args = role + ": " + args;
                }

                commands.Add(new ModelfileCommand(commandName, args));
                break;
            }

            default:
                throw new ModelfileParseException(0, ErrUnexpectedEof);
        }

        foreach (var command in commands)
        {
            if (command.Name == "model")
            {
                return commands;
            }
        }

        throw new ModelfileParseException(0, ErrMissingFrom);
    }

    /// <summary>
    /// The state machine's transition for one character. Where Ollama returns an error, this
    /// throws: an invalid command carries the line number, and an unfinished command carries the
    /// text buffered so far, as Ollama's messages do.
    /// </summary>
    private static ModelfileParserState ParseRuneForState(Rune rune, ModelfileParserState state,
        int currentLine, StringBuilder buffer, out Rune current)
    {
        current = default;

        switch (state)
        {
            case ModelfileParserState.Nil:
                if (rune.Value == '#')
                {
                    return ModelfileParserState.Comment;
                }

                if (IsSpace(rune) || IsNewline(rune))
                {
                    return ModelfileParserState.Nil;
                }

                current = rune;
                return ModelfileParserState.Name;

            case ModelfileParserState.Name:
                if (IsAlpha(rune))
                {
                    current = rune;
                    return ModelfileParserState.Name;
                }

                if (IsSpace(rune))
                {
                    return ModelfileParserState.Value;
                }

                throw new ModelfileParseException(currentLine, ErrInvalidCommand);

            case ModelfileParserState.Value:
                current = rune;
                if (IsNewline(rune) || IsSpace(rune))
                {
                    return ModelfileParserState.Nil;
                }

                return ModelfileParserState.Value;

            case ModelfileParserState.Parameter:
                if (IsAlpha(rune) || IsNumber(rune) || rune.Value == '_')
                {
                    current = rune;
                    return ModelfileParserState.Parameter;
                }

                if (IsSpace(rune))
                {
                    return ModelfileParserState.Value;
                }

                throw new ModelfileParseException(0, ErrUnexpectedEof + ": " + buffer);

            case ModelfileParserState.Message:
                if (IsAlpha(rune))
                {
                    current = rune;
                    return ModelfileParserState.Message;
                }

                if (IsSpace(rune))
                {
                    return ModelfileParserState.Value;
                }

                throw new ModelfileParseException(0, ErrUnexpectedEof + ": " + buffer);

            case ModelfileParserState.Comment:
                if (IsNewline(rune))
                {
                    return ModelfileParserState.Nil;
                }

                return ModelfileParserState.Comment;

            default:
                throw new ModelfileParseException(currentLine, string.Empty);
        }
    }

    private static string StripByteOrderMark(string text)
    {
        return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
    }

    private static ModelMessage ToMessage(string args)
    {
        var separator = args.IndexOf(": ", StringComparison.Ordinal);
        return separator < 0
            ? new ModelMessage(args, string.Empty)
            : new ModelMessage(args.Substring(0, separator), args.Substring(separator + 2));
    }

    private static bool IsDeprecatedParameter(string name)
    {
        foreach (var deprecated in DeprecatedParameterNames)
        {
            if (deprecated == name)
            {
                return true;
            }
        }

        return false;
    }

    private static int ParseInt(string name, string value)
    {
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ModelfileParseException(0, $"invalid int value [{value}]");
        }

        if (parsed > int.MaxValue || parsed < int.MinValue)
        {
            throw new ModelfileParseException(0,
                $"int value [{value}] for parameter '{name}' does not fit in a 32-bit integer");
        }

        return (int)parsed;
    }

    private static float ParseFloat(string value)
    {
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowExponent;
        if (!float.TryParse(value, styles, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ModelfileParseException(0, $"invalid float value [{value}]");
        }

        return parsed;
    }

    private static bool ParseBool(string value)
    {
        if (!GoBool.TryParse(value, out var parsed))
        {
            throw new ModelfileParseException(0, $"invalid bool value [{value}]");
        }

        return parsed;
    }

    private static bool IsAlpha(Rune rune)
    {
        return (rune.Value >= 'a' && rune.Value <= 'z') || (rune.Value >= 'A' && rune.Value <= 'Z');
    }

    private static bool IsNumber(Rune rune)
    {
        return rune.Value >= '0' && rune.Value <= '9';
    }

    private static bool IsSpace(Rune rune)
    {
        return rune.Value == ' ' || rune.Value == '\t';
    }

    private static bool IsNewline(Rune rune)
    {
        return rune.Value == '\r' || rune.Value == '\n';
    }

    /// <summary>
    /// Go's definition of a printable character: letters, marks, numbers, punctuation, symbols and
    /// the ASCII space. Every other character, including tab, newline and the byte order mark, is
    /// left out of the buffer.
    /// </summary>
    private static bool IsPrint(Rune rune)
    {
        if (rune.Value == ' ')
        {
            return true;
        }

        switch (Rune.GetUnicodeCategory(rune))
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.OtherNumber:
            case UnicodeCategory.ConnectorPunctuation:
            case UnicodeCategory.DashPunctuation:
            case UnicodeCategory.OpenPunctuation:
            case UnicodeCategory.ClosePunctuation:
            case UnicodeCategory.InitialQuotePunctuation:
            case UnicodeCategory.FinalQuotePunctuation:
            case UnicodeCategory.OtherPunctuation:
            case UnicodeCategory.MathSymbol:
            case UnicodeCategory.CurrencySymbol:
            case UnicodeCategory.ModifierSymbol:
            case UnicodeCategory.OtherSymbol:
                return true;
            default:
                return false;
        }
    }

    private static bool IsValidMessageRole(string role)
    {
        return role == "system" || role == "user" || role == "assistant";
    }

    private static bool IsValidCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "from":
            case "license":
            case "template":
            case "system":
            case "adapter":
            case "draft":
            case "renderer":
            case "parser":
            case "parameter":
            case "message":
            case "requires":
                return true;
            default:
                return false;
        }
    }
}
