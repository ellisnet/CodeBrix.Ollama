using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/lex.go (BSD-3-Clause);

/// <summary>
/// The keyword table the Go text/template scanner consults for every bare identifier.
/// </summary>
internal static class GoTemplateKeywords
{
    private static readonly Dictionary<string, GoTemplateItemType> Keywords = new Dictionary<string, GoTemplateItemType>
    {
        { ".", GoTemplateItemType.Dot },
        { "block", GoTemplateItemType.Block },
        { "break", GoTemplateItemType.Break },
        { "continue", GoTemplateItemType.Continue },
        { "define", GoTemplateItemType.Define },
        { "else", GoTemplateItemType.Else },
        { "end", GoTemplateItemType.End },
        { "if", GoTemplateItemType.If },
        { "range", GoTemplateItemType.Range },
        { "nil", GoTemplateItemType.Nil },
        { "template", GoTemplateItemType.Template },
        { "with", GoTemplateItemType.With },
    };

    /// <summary>
    /// Looks a word up in the keyword table.
    /// </summary>
    /// <param name="word">The word the scanner read.</param>
    /// <returns>The keyword's token type, or <see cref="GoTemplateItemType.Error"/> when the word is not a
    /// keyword (which, being below <see cref="GoTemplateItemType.Keyword"/>, is how callers tell).</returns>
    internal static GoTemplateItemType Lookup(string word)
        => word != null && Keywords.TryGetValue(word, out GoTemplateItemType type) ? type : GoTemplateItemType.Error;
}
