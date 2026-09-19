using System;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// How the chat's commands read what was typed after their name. Everything is read from the
/// line VERBATIM rather than from the tokens, because a command whose argument is text for the
/// model has to keep the quotes and the spacing the user wrote.
/// </summary>
internal static class CommandArguments
{
    /// <summary>What a user adds to a command to say "do it", rather than "tell me what it would do".</summary>
    internal const string ConfirmFlag = "-y";

    /// <summary>
    /// Takes the confirmation off the front of a command's arguments. It is accepted BEFORE the
    /// argument and nowhere else, so text that happens to contain the same two characters can
    /// never be read as a confirmation.
    /// </summary>
    /// <param name="rawArguments">The line after the command's name, exactly as it was typed.</param>
    /// <param name="remainder">What is left once the confirmation has been taken off.</param>
    /// <returns>True when the user confirmed.</returns>
    internal static bool TakeConfirmation(string rawArguments, out string remainder)
    {
        remainder = rawArguments ?? string.Empty;

        if (!remainder.StartsWith(ConfirmFlag, StringComparison.Ordinal))
        {
            return false;
        }

        if (remainder.Length > ConfirmFlag.Length && !char.IsWhiteSpace(remainder[ConfirmFlag.Length]))
        {
            return false;
        }

        remainder = remainder.Substring(ConfirmFlag.Length).TrimStart();

        return true;
    }

    /// <summary>
    /// Takes ONE pair of surrounding double quotes off a piece of text, leaving everything
    /// inside it - quotes included - exactly as it was written.
    /// </summary>
    /// <param name="text">The text as it was typed.</param>
    /// <returns>The text without its outermost pair of quotes.</returns>
    internal static string StripOuterQuotes(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 2)
        {
            return text ?? string.Empty;
        }

        return text[0] == '"' && text[text.Length - 1] == '"'
            ? text.Substring(1, text.Length - 2)
            : text;
    }
}
