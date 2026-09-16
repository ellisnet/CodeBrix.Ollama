// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: parser/parser.go at commit a43fad18.
using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One command from a Modelfile: the keyword Ollama stores it under, and its argument text with
/// quoting already removed.
/// </summary>
/// <remarks>
/// <see cref="Name"/> carries Ollama's internal spelling, not the keyword as written. A FROM line
/// is stored as "model"; every other keyword is stored lower-cased ("license", "template",
/// "system", "adapter", "draft", "renderer", "parser", "requires", "message"); a PARAMETER line is
/// stored under the bare parameter name, so "PARAMETER temperature 0.5" becomes
/// <see cref="Name"/> "temperature" with <see cref="Args"/> "0.5".
/// </remarks>
public sealed class ModelfileCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelfileCommand"/> class.
    /// </summary>
    /// <param name="name">Ollama's internal spelling of the keyword; see the type remarks.</param>
    /// <param name="args">The argument text, with any quoting already removed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="args"/> is null.</exception>
    public ModelfileCommand(string name, string args)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Args = args ?? throw new ArgumentNullException(nameof(args));
    }

    /// <summary>
    /// Ollama's internal spelling of the keyword; see the type remarks.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The argument text, with any quoting already removed. For a MESSAGE command this is the role
    /// and the content joined by ": ".
    /// </summary>
    public string Args { get; }

    /// <summary>
    /// Renders the command back to a Modelfile line, re-quoting the argument where Ollama would.
    /// </summary>
    /// <returns>The line, without a trailing newline.</returns>
    public override string ToString()
    {
        switch (Name)
        {
            case "model":
                return "FROM " + Args;
            case "license":
            case "template":
            case "system":
            case "adapter":
            case "renderer":
            case "parser":
            case "requires":
            case "draft":
                return Name.ToUpperInvariant() + " " + Modelfile.Quote(Args);
            case "message":
            {
                var separator = Args.IndexOf(": ", StringComparison.Ordinal);
                var role = separator < 0 ? Args : Args.Substring(0, separator);
                var message = separator < 0 ? string.Empty : Args.Substring(separator + 2);
                return "MESSAGE " + role + " " + Modelfile.Quote(message);
            }
            default:
                return "PARAMETER " + Name + " " + Modelfile.Quote(Args);
        }
    }
}
