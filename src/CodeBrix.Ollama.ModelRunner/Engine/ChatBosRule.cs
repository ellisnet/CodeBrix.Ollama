using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The one rule that decides how a rendered chat prompt is tokenized: whether the tokenizer may add the
/// vocabulary's beginning-of-sequence token, or whether the template has already written it into the text.
/// </summary>
/// <remarks>
/// <para>
/// A duplicated beginning-of-sequence token is the classic sharp edge of running a chat template by hand.
/// Some templates open with <c>{{ bos_token }}</c> and some do not, and the vocabulary separately declares
/// whether one belongs at the start of a sequence. Tokenizing with <c>add_special</c> always true gives the
/// first kind of template two of them, which measurably changes what the model says; tokenizing with it
/// always false gives the second kind none.
/// </para>
/// <para>
/// The rule is therefore: the text wins. When the vocabulary asks for a beginning-of-sequence token and the
/// rendered prompt already starts with that token's own text, the tokenizer is told not to add one - the
/// text is tokenized with <c>parse_special</c> on, so the literal it starts with becomes the real token.
/// In every other case the tokenizer is left to add one, which it only does when the vocabulary asks.
/// </para>
/// <para>
/// This is a pure function of three values, which is what makes it testable without a model.
/// </para>
/// </remarks>
internal static class ChatBosRule
{
    /// <summary>Whether the rendered prompt should be tokenized with special tokens added.</summary>
    /// <param name="renderedPrompt">The prompt the chat template produced. <see langword="null"/> is treated as empty.</param>
    /// <param name="vocabularyAddsBos">Whether the vocabulary declares that a sequence starts with the token.</param>
    /// <param name="bosText">The beginning-of-sequence token's own text, or <see langword="null"/> when the vocabulary has none.</param>
    /// <returns><see langword="true"/> to tokenize with <c>add_special</c>, <see langword="false"/> to leave it to the text.</returns>
    public static bool AddSpecialTokens(string renderedPrompt, bool vocabularyAddsBos, string bosText)
    {
        if (!vocabularyAddsBos) return true;
        if (string.IsNullOrEmpty(bosText)) return true;

        string prompt = renderedPrompt ?? string.Empty;
        return !prompt.StartsWith(bosText, StringComparison.Ordinal);
    }
}
