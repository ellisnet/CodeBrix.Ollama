using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: src/transformers/models/gpt2/tokenization_gpt2.py@v4.57.6

/// <summary>
/// The first cut a byte-level byte-pair encoder makes: text into the pieces the merge table is allowed to
/// work inside. A merge never crosses one of these boundaries, so this is where a word keeps the space in
/// front of it and where a run of digits is kept apart from the letters beside it.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE, as the published tokenizer writes it, is one regular expression:
/// <c>'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+</c>, scanned left to right,
/// each alternative tried in the order it is written. Read out loud: seven English contractions; then a run
/// of letters, a run of numbers or a run of anything else, each allowed to take one space in front of it;
/// then a run of whitespace with its LAST character left behind for whatever follows; then a run of
/// whitespace.
/// </para>
/// <para>
/// IT IS WRITTEN OUT HERE AS A SCAN RATHER THAN AS A REGULAR EXPRESSION, and that is a correctness decision
/// rather than a performance one. The published tokenizer runs on a regular-expression engine that works in
/// CODE POINTS; .NET's works in UTF-16 code units, where a character outside the basic multilingual plane is
/// a surrogate pair and <c>\p{L}</c> therefore matches neither half of it. A Chinese character from the
/// extension B block, or a mathematical italic letter, would be a letter to the published tokenizer and
/// "anything else" to a .NET regular expression of the same text - different pieces, different merges,
/// different token numbers. The scan below reads one code point at a time and so agrees everywhere. On text
/// inside the basic multilingual plane the two are the same thing, and the test suite holds this scan to a
/// compiled .NET regular expression of exactly that pattern to prove it.
/// </para>
/// <para>
/// The whitespace class is .NET's own <c>\s</c> - the five ASCII whitespace controls, the next-line
/// character, and the three Unicode separator categories - which matches the published engine's on every
/// assigned code point.
/// </para>
/// </remarks>
internal static class Gpt2PreTokenizer
{
    //The seven contractions, longest first so that 'l' + 'l' is never taken as two pieces.
    private static readonly string[] Contractions = { "'re", "'ve", "'ll", "'s", "'t", "'m", "'d" };

    /// <summary>Cuts text into the pieces a merge table may work inside.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The pieces, in order; together they are the whole of the text.</returns>
    internal static List<string> Split(string text)
    {
        List<string> pieces = new List<string>();
        if (string.IsNullOrEmpty(text)) return pieces;

        int at = 0;
        int length = text.Length;

        while (at < length)
        {
            int contraction = MatchContraction(text, at);
            if (contraction > 0)
            {
                pieces.Add(text.Substring(at, contraction));
                at += contraction;
                continue;
            }

            //One leading SPACE - the literal character, not whitespace in general - may join the run that
            //follows it, which is what makes " word" one piece and the reason a word at the start of a
            //sentence is a different token from the same word in the middle of one.
            int after = text[at] == ' ' ? at + 1 : at;
            if (after < length)
            {
                Rune rune = RuneAt(text, after);
                if (IsLetter(rune))
                {
                    at = Take(text, pieces, at, after, IsLetter);
                    continue;
                }

                if (IsNumber(rune))
                {
                    at = Take(text, pieces, at, after, IsNumber);
                    continue;
                }

                if (!IsWhiteSpace(rune))
                {
                    at = Take(text, pieces, at, after, IsOther);
                    continue;
                }
            }

            at = TakeWhiteSpace(text, pieces, at);
        }

        return pieces;
    }

    /// <summary>Whether a code point is a letter, in the sense the pattern's <c>\p{L}</c> has.</summary>
    /// <param name="rune">The code point.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsLetter(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.UppercaseLetter => true,
        UnicodeCategory.LowercaseLetter => true,
        UnicodeCategory.TitlecaseLetter => true,
        UnicodeCategory.ModifierLetter => true,
        UnicodeCategory.OtherLetter => true,
        _ => false,
    };

    /// <summary>Whether a code point is a number, in the sense the pattern's <c>\p{N}</c> has.</summary>
    /// <param name="rune">The code point.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsNumber(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.DecimalDigitNumber => true,
        UnicodeCategory.LetterNumber => true,
        UnicodeCategory.OtherNumber => true,
        _ => false,
    };

    /// <summary>Whether a code point is whitespace, in the sense the pattern's <c>\s</c> has.</summary>
    /// <param name="rune">The code point.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsWhiteSpace(Rune rune)
    {
        int value = rune.Value;

        //Tab, line feed, vertical tab, form feed, carriage return, and the next-line character.
        if (value >= 0x09 && value <= 0x0D) return true;
        if (value == 0x85) return true;

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.SpaceSeparator => true,
            UnicodeCategory.LineSeparator => true,
            UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };
    }

    private static bool IsOther(Rune rune) => !IsWhiteSpace(rune) && !IsLetter(rune) && !IsNumber(rune);

    /// <summary>
    /// Reads the code point at a position, treating a surrogate that has lost its partner as an ordinary
    /// character of no category - which is what the published engine does with one as well.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="at">The position.</param>
    /// <returns>The code point.</returns>
    private static Rune RuneAt(string text, int at)
    {
        if (Rune.TryGetRuneAt(text, at, out Rune rune)) return rune;
        return Rune.ReplacementChar;
    }

    private static int Width(string text, int at) =>
        Rune.TryGetRuneAt(text, at, out Rune rune) ? rune.Utf16SequenceLength : 1;

    private static int Take(string text, List<string> pieces, int start, int from, Func<Rune, bool> wanted)
    {
        int at = from;
        while (at < text.Length)
        {
            Rune rune = RuneAt(text, at);
            if (!wanted(rune)) break;
            at += Width(text, at);
        }

        pieces.Add(text.Substring(start, at - start));
        return at;
    }

    private static int TakeWhiteSpace(string text, List<string> pieces, int start)
    {
        int at = start;
        int lastRuneAt = start;
        while (at < text.Length)
        {
            Rune rune = RuneAt(text, at);
            if (!IsWhiteSpace(rune)) break;
            lastRuneAt = at;
            at += Width(text, at);
        }

        //`\s+(?!\S)` is the alternative tried first, and what it does in practice is leave the LAST character
        //of a run behind for whatever follows it - which is how " a" keeps its space. It cannot match when
        //that would leave it nothing, so a run of one before an ordinary character is taken whole by the
        //plain `\s+` after it. A run that reaches the end of the text has nothing following it and is taken
        //whole either way.
        int end = at >= text.Length || lastRuneAt == start ? at : lastRuneAt;

        //Nothing reaches here that is not whitespace, so this never fires; it is here because a piece of no
        //length would be a scan that never moved on.
        if (end <= start) end = start + Width(text, start);

        pieces.Add(text.Substring(start, end - start));
        return end;
    }

    private static int MatchContraction(string text, int at)
    {
        if (text[at] != '\'') return 0;

        foreach (string contraction in Contractions)
        {
            if (at + contraction.Length > text.Length) continue;
            if (string.CompareOrdinal(text, at, contraction, 0, contraction.Length) == 0)
            {
                return contraction.Length;
            }
        }

        return 0;
    }
}
