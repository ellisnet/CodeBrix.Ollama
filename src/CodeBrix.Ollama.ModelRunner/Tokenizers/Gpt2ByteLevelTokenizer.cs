using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: src/transformers/models/gpt2/tokenization_gpt2.py@v4.57.6

/// <summary>
/// A byte-level byte-pair encoder read out of a bundle's <c>vocab.json</c> and <c>merges.txt</c>: text to
/// token numbers and token numbers back to text, with nothing installed.
/// </summary>
/// <remarks>
/// <para>
/// BYTE-LEVEL means the alphabet is the 256 byte values rather than the characters of any language, so there
/// is no character it cannot write down and no "unknown" answer in practice: text becomes UTF-8 bytes, each
/// byte becomes the printable symbol <see cref="Gpt2ByteTable"/> gives it, and the merge table then joins
/// those symbols in rank order until no pair of neighbours is in the table. Decoding runs the same steps
/// backwards.
/// </para>
/// <para>
/// THE MERGE TABLE IS READ BY THE RULE THE REST OF THIS REPOSITORY READS IT WITH -
/// <c>CodeBrix.Ollama.Core</c>'s primitive, which drops a first line only when it begins with <c>#</c>. A
/// bundle written by a model builder carries that header, because the publisher's own save path writes one,
/// so the table this reads is the same table, entry for entry, that the publisher's own tokenizer held.
/// </para>
/// <para>
/// A run of text is cut into pieces first (<see cref="Gpt2PreTokenizer"/>) and merges never cross a piece
/// boundary, which is why the same word is a different token with and without the space in front of it. The
/// result of merging one piece is cached, because a decoder's prompt repeats the same short pieces many
/// times.
/// </para>
/// </remarks>
internal sealed class Gpt2ByteLevelTokenizer
{
    private readonly Dictionary<string, int> _encoder;
    private readonly string[] _decoder;
    private readonly Dictionary<string, int> _ranks;
    private readonly Gpt2TokenizerSettings _settings;
    private readonly Gpt2AddedToken[] _added;
    private readonly HashSet<int> _special = new HashSet<int>();
    private readonly Dictionary<int, string> _addedById = new Dictionary<int, string>();
    private readonly Dictionary<string, int[]> _cache = new Dictionary<string, int[]>(StringComparer.Ordinal);
    private readonly object _gate = new object();
    private readonly int _unknown = -1;
    private readonly int _beginningOfSequence = -1;

    /// <summary>Builds the tokenizer from a vocabulary, a merge table and what the bundle said about them.</summary>
    /// <param name="vocabulary">The symbol string of every token against its number.</param>
    /// <param name="merges">The merges in rank order, each one two pieces separated by a single space.</param>
    /// <param name="settings">What the configuration files said.</param>
    /// <exception cref="ModelLoadException">The vocabulary is empty or holds a number below nought.</exception>
    internal Gpt2ByteLevelTokenizer(
        IReadOnlyDictionary<string, int> vocabulary,
        IReadOnlyList<string> merges,
        Gpt2TokenizerSettings settings)
    {
        _settings = settings ?? Gpt2TokenizerSettings.Default();

        if (vocabulary == null || vocabulary.Count == 0)
        {
            throw new ModelLoadException(
                "The bundle's vocab.json holds no tokens, so its tokenizer cannot be built.");
        }

        _encoder = new Dictionary<string, int>(vocabulary.Count, StringComparer.Ordinal);
        int highest = -1;
        foreach (KeyValuePair<string, int> entry in vocabulary)
        {
            if (entry.Value < 0)
            {
                throw new ModelLoadException(
                    "The bundle's vocab.json gives the token '" + entry.Key + "' the number "
                    + entry.Value.ToString(CultureInfo.InvariantCulture)
                    + ", and a token number is never below nought.");
            }

            _encoder[entry.Key] = entry.Value;
            if (entry.Value > highest) highest = entry.Value;
        }

        _decoder = new string[highest + 1];
        foreach (KeyValuePair<string, int> entry in vocabulary) _decoder[entry.Value] = entry.Key;

        _ranks = new Dictionary<string, int>(
            merges == null ? 0 : merges.Count, StringComparer.Ordinal);
        if (merges != null)
        {
            for (int rank = 0; rank < merges.Count; rank++)
            {
                //A merge that is written twice keeps its FIRST rank, which is the rank the publisher's own
                //reader gives it as well.
                _ranks.TryAdd(merges[rank], rank);
            }
        }

        List<Gpt2AddedToken> added = new List<Gpt2AddedToken>(_settings.Added);
        added.Sort(ByLengthThenText);
        _added = added.ToArray();
        foreach (Gpt2AddedToken token in _added)
        {
            _addedById[token.Id] = token.Content;
            if (token.IsSpecial) _special.Add(token.Id);
        }

        if (_settings.Unknown != null && _encoder.TryGetValue(_settings.Unknown, out int unknown))
        {
            _unknown = unknown;
        }

        if (_settings.BeginningOfSequence != null
            && _encoder.TryGetValue(_settings.BeginningOfSequence, out int beginning))
        {
            _beginningOfSequence = beginning;
        }

        VocabularySize = _decoder.Length;
        MergeCount = _ranks.Count;
    }

    /// <summary>The number of token numbers the vocabulary reaches, which is the largest one plus one.</summary>
    internal int VocabularySize { get; }

    /// <summary>How many merges the table holds.</summary>
    internal int MergeCount { get; }

    /// <summary>Turns text into token numbers.</summary>
    /// <param name="text">The text.</param>
    /// <param name="addSpecialTokens">
    /// Whether the beginning-of-sequence token is put in front, when the bundle says its tokenizer does that.
    /// </param>
    /// <param name="parseSpecialTokens">
    /// Whether the text of an added token is recognized as that token rather than encoded as ordinary text.
    /// </param>
    /// <returns>The token numbers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InferenceException">A symbol the merges produced is not in the vocabulary and the bundle names no unknown token.</exception>
    internal IReadOnlyList<int> Encode(string text, bool addSpecialTokens, bool parseSpecialTokens)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));

        List<int> tokens = new List<int>();
        if (addSpecialTokens && _settings.AddBeginningOfSequence && _beginningOfSequence >= 0)
        {
            tokens.Add(_beginningOfSequence);
        }

        string prepared = _settings.AddPrefixSpace ? " " + text : text;
        if (prepared.Length == 0) return tokens;

        if (!parseSpecialTokens || _added.Length == 0)
        {
            EncodeOrdinary(prepared, tokens);
            return tokens;
        }

        int at = 0;
        int plain = 0;
        while (at < prepared.Length)
        {
            Gpt2AddedToken match = MatchAdded(prepared, at);
            if (match == null)
            {
                at++;
                continue;
            }

            if (at > plain) EncodeOrdinary(prepared.Substring(plain, at - plain), tokens);
            tokens.Add(match.Id);
            at += match.Content.Length;
            plain = at;
        }

        if (plain < prepared.Length) EncodeOrdinary(prepared.Substring(plain), tokens);
        return tokens;
    }

    /// <summary>Turns token numbers back into text.</summary>
    /// <param name="tokens">The token numbers.</param>
    /// <param name="renderSpecialTokens">Whether a special token is written out as its text rather than dropped.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
    internal string Decode(IReadOnlyList<int> tokens, bool renderSpecialTokens)
    {
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));

        StringBuilder text = new StringBuilder();
        List<byte> run = new List<byte>();

        foreach (int token in tokens)
        {
            if (_addedById.TryGetValue(token, out string content))
            {
                //An added token's text is its own; it was never byte-level encoded, so it is written out
                //rather than decoded. What is already in hand is finished first, so that a byte sequence
                //split across tokens is never cut in half by one.
                Flush(text, run);
                if (renderSpecialTokens || !_special.Contains(token)) text.Append(content);
                continue;
            }

            AppendBytes(run, token);
        }

        Flush(text, run);
        return text.ToString();
    }

    /// <summary>
    /// The bytes one token contributes to a stream of text, for a caller assembling UTF-8 across token
    /// boundaries as it goes.
    /// </summary>
    /// <param name="token">The token number.</param>
    /// <param name="renderSpecialTokens">Whether a special token contributes its text rather than nothing.</param>
    /// <returns>The bytes, which are empty for a special token that is being dropped.</returns>
    internal byte[] Bytes(int token, bool renderSpecialTokens)
    {
        if (_addedById.TryGetValue(token, out string content))
        {
            if (!renderSpecialTokens && _special.Contains(token)) return Array.Empty<byte>();
            return Encoding.UTF8.GetBytes(content);
        }

        List<byte> run = new List<byte>();
        AppendBytes(run, token);
        return run.ToArray();
    }

    /// <summary>Whether a token number is one the bundle calls special.</summary>
    /// <param name="token">The token number.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal bool IsSpecial(int token) => _special.Contains(token);

    /// <summary>The symbol string one token number is written as, or <see langword="null"/> when there is none.</summary>
    /// <param name="token">The token number.</param>
    /// <returns>The symbol string.</returns>
    internal string Symbol(int token) =>
        token >= 0 && token < _decoder.Length ? _decoder[token] : null;

    private static int ByLengthThenText(Gpt2AddedToken left, Gpt2AddedToken right)
    {
        int byLength = right.Content.Length.CompareTo(left.Content.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(left.Content, right.Content);
    }

    private Gpt2AddedToken MatchAdded(string text, int at)
    {
        foreach (Gpt2AddedToken token in _added)
        {
            string content = token.Content;
            if (content.Length == 0 || at + content.Length > text.Length) continue;
            if (string.CompareOrdinal(text, at, content, 0, content.Length) == 0) return token;
        }

        return null;
    }

    private void EncodeOrdinary(string text, List<int> tokens)
    {
        foreach (string piece in Gpt2PreTokenizer.Split(text))
        {
            int[] cached;
            lock (_gate)
            {
                _cache.TryGetValue(piece, out cached);
            }

            if (cached == null)
            {
                cached = Merge(piece);
                lock (_gate)
                {
                    _cache[piece] = cached;
                }
            }

            tokens.AddRange(cached);
        }
    }

    private int[] Merge(string piece)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(piece);
        string symbols = Gpt2ByteTable.Encode(utf8, utf8.Length);

        List<string> word = new List<string>(symbols.Length);
        foreach (char symbol in symbols) word.Add(symbol.ToString());

        while (word.Count > 1)
        {
            int bestRank = int.MaxValue;
            int bestAt = -1;
            for (int i = 0; i + 1 < word.Count; i++)
            {
                if (!_ranks.TryGetValue(word[i] + " " + word[i + 1], out int rank)) continue;
                if (rank >= bestRank) continue;

                bestRank = rank;
                bestAt = i;
            }

            if (bestAt < 0) break;

            string left = word[bestAt];
            string right = word[bestAt + 1];
            List<string> merged = new List<string>(word.Count);
            int at = 0;
            while (at < word.Count)
            {
                if (at + 1 < word.Count
                    && string.Equals(word[at], left, StringComparison.Ordinal)
                    && string.Equals(word[at + 1], right, StringComparison.Ordinal))
                {
                    merged.Add(left + right);
                    at += 2;
                    continue;
                }

                merged.Add(word[at]);
                at++;
            }

            word = merged;
        }

        int[] tokens = new int[word.Count];
        for (int i = 0; i < word.Count; i++) tokens[i] = Number(word[i]);
        return tokens;
    }

    private int Number(string symbol)
    {
        if (_encoder.TryGetValue(symbol, out int token)) return token;
        if (_unknown >= 0) return _unknown;

        throw new InferenceException(
            "The bundle's vocabulary holds neither the symbol the merges produced nor an unknown token to put"
            + " in its place, so this text cannot be tokenized.");
    }

    private void AppendBytes(List<byte> run, int token)
    {
        string symbol = Symbol(token);
        if (symbol == null) return;

        foreach (char character in symbol)
        {
            //A symbol the byte table does not name cannot have come from byte-level text; it is dropped
            //rather than guessed at, which is what the published decoder's own error handling amounts to.
            if (Gpt2ByteTable.TryByte(character, out byte value)) run.Add(value);
        }
    }

    private static void Flush(StringBuilder text, List<byte> run)
    {
        if (run.Count == 0) return;

        //One decode for the whole run, so that a character whose bytes were split across two tokens comes
        //out whole; anything genuinely malformed becomes the replacement character, which is what the
        //published decoder's "replace" setting does.
        text.Append(Encoding.UTF8.GetString(run.ToArray()));
        run.Clear();
    }
}
