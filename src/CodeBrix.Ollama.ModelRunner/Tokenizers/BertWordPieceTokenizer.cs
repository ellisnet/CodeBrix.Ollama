using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CodeBrix.Ollama.ModelRunner; //was previously: transformers/models/bert/tokenization_bert.py (algorithm; Apache-2.0)

/// <summary>Uncased BERT normalization and greedy WordPiece, operating on Unicode scalar values.</summary>
internal sealed class BertWordPieceTokenizer
{
    private readonly Dictionary<string, long> _vocabulary;
    private readonly string[] _special;
    private readonly int _maxWordCharacters;

    internal BertWordPieceTokenizer(JsonElement root)
    {
        if (!root.GetProperty("lowercase").GetBoolean() || !root.GetProperty("stripAccents").GetBoolean()
            || !root.GetProperty("tokenizeChineseCharacters").GetBoolean())
        {
            throw new ModelLoadException("Expected the uncased MuseCoco BERT tokenizer contract.");
        }
        _maxWordCharacters = MuseCocoBundle.Positive(root, "maxWordCharacters", 1000);
        _vocabulary = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (JsonElement item in root.GetProperty("vocabulary").EnumerateArray())
        {
            string word = item.GetString();
            if (string.IsNullOrEmpty(word) || !_vocabulary.TryAdd(word, _vocabulary.Count))
            {
                throw new ModelLoadException("The BERT vocabulary contains an empty or duplicate entry.");
            }
        }
        _special = root.GetProperty("specialTokens").EnumerateArray().Select(v => v.GetString()).ToArray();
        if (_special.Any(s => string.IsNullOrEmpty(s) || !_vocabulary.ContainsKey(s)))
        {
            throw new ModelLoadException("A BERT special token is absent from the vocabulary.");
        }
        foreach (string word in new[] { "[CLS]", "[SEP]", "[UNK]", "[PAD]", "[MASK]" })
        {
            if (!_special.Contains(word, StringComparer.Ordinal))
            {
                throw new ModelLoadException("The BERT tokenizer is missing special token " + word);
            }
        }
        // Longest first is deterministic even for a custom vocabulary with overlapping special tokens.
        Array.Sort(_special, (left, right) => right.Length.CompareTo(left.Length));
    }

    internal BertWordPieceEncoding Encode(string text, int maximumLength, CancellationToken cancellationToken)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (maximumLength < 2) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        cancellationToken.ThrowIfCancellationRequested();
        var ids = new List<long> { _vocabulary["[CLS]"] };
        bool truncated = false;
        foreach (string word in BasicTokens(text, cancellationToken))
        {
            foreach (long id in WordPieces(word))
            {
                if (ids.Count == maximumLength - 1)
                {
                    truncated = true;
                    break;
                }
                ids.Add(id);
            }
            if (truncated) break;
        }
        ids.Add(_vocabulary["[SEP]"]);
        return new BertWordPieceEncoding(ids.ToArray(), truncated);
    }

    private IEnumerable<string> BasicTokens(string text, CancellationToken cancellationToken)
    {
        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            string found = null;
            if (text[index] == '[')
            {
                foreach (string special in _special)
                {
                    if (text.AsSpan(index).StartsWith(special.AsSpan(), StringComparison.Ordinal))
                    {
                        found = special;
                        break;
                    }
                }
            }
            if (found == null) continue;
            foreach (string word in NormalizeTokens(text.Substring(start, index - start), cancellationToken)) yield return word;
            yield return found;
            index += found.Length - 1;
            start = index + 1;
        }
        foreach (string word in NormalizeTokens(text.Substring(start), cancellationToken)) yield return word;
    }

    private static IEnumerable<string> NormalizeTokens(string text, CancellationToken cancellationToken)
    {
        var cleaned = new StringBuilder();
        int visited = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if ((visited++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            int value = rune.Value;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (value == 0 || value == 0xfffd) continue;
            if (value == 9 || value == 10 || value == 13 || Rune.IsWhiteSpace(rune))
            {
                cleaned.Append(' ');
            }
            else if (category != UnicodeCategory.Control && category != UnicodeCategory.Format)
            {
                if (IsChinese(value)) cleaned.Append(' ');
                cleaned.Append(rune.ToString());
                if (IsChinese(value)) cleaned.Append(' ');
            }
        }
        var current = new StringBuilder();
        foreach (Rune rune in cleaned.ToString().Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if ((visited++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            bool punctuation = IsPunctuation(rune.Value, category);
            if (Rune.IsWhiteSpace(rune) || punctuation)
            {
                if (current.Length != 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (punctuation) yield return rune.ToString();
            }
            else
            {
                current.Append(Rune.ToLowerInvariant(rune).ToString());
            }
        }
        if (current.Length != 0) yield return current.ToString();
    }

    private IEnumerable<long> WordPieces(string word)
    {
        if (_special.Contains(word, StringComparer.Ordinal))
        {
            yield return _vocabulary[word];
            yield break;
        }
        var boundaries = new List<int> { 0 };
        int offset = 0;
        foreach (Rune rune in word.EnumerateRunes())
        {
            offset += rune.Utf16SequenceLength;
            boundaries.Add(offset);
        }
        var pieces = new List<long>();
        if (boundaries.Count - 1 <= _maxWordCharacters)
        {
            int start = 0;
            while (start < boundaries.Count - 1)
            {
                int end = boundaries.Count - 1;
                for (; end > start; end--)
                {
                    string candidate = (start == 0 ? string.Empty : "##") + word.Substring(boundaries[start], boundaries[end] - boundaries[start]);
                    if (_vocabulary.TryGetValue(candidate, out long id))
                    {
                        pieces.Add(id);
                        break;
                    }
                }
                if (end == start)
                {
                    pieces.Clear();
                    break;
                }
                start = end;
            }
        }
        if (pieces.Count == 0)
        {
            yield return _vocabulary["[UNK]"];
        }
        else
        {
            foreach (long piece in pieces) yield return piece;
        }
    }

    private static bool IsChinese(int value) => value is >= 0x4e00 and <= 0x9fff
        or >= 0x3400 and <= 0x4dbf or >= 0x20000 and <= 0x2a6df or >= 0x2a700 and <= 0x2b73f
        or >= 0x2b740 and <= 0x2b81f or >= 0x2b820 and <= 0x2ceaf or >= 0xf900 and <= 0xfaff
        or >= 0x2f800 and <= 0x2fa1f;

    private static bool IsPunctuation(int value, UnicodeCategory category) => value is >= 33 and <= 47
        or >= 58 and <= 64 or >= 91 and <= 96 or >= 123 and <= 126
        || category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
        or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
        or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
}
