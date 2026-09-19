using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221, gguf-py/gguf/vocab.py@b10221;

/// <summary>
/// Turns a GPT-2 byte-level BPE tokenizer - <c>vocab.json</c> plus <c>merges.txt</c> - into the vocabulary a
/// GGUF file carries.
/// </summary>
/// <remarks>
/// <para>
/// Three rules here are ported rather than invented, and each of them changes the bytes of the output. Every
/// identifier below the configured vocabulary size gets an entry, so a vocabulary shorter than the model's
/// embedding matrix is padded with <c>[PAD&lt;n&gt;]</c> placeholders of type unused. An entry the publisher
/// added by hand is a control token when it is marked special and a user-defined token otherwise, and only an
/// added entry can be either - the rest of the vocabulary is normal. And the pre-tokenizer written into the
/// file is <c>gpt-2</c>, which is what the engine's own tokenizer implements.
/// </para>
/// <para>
/// This version reads the file set the GPT-2 path uses: <c>vocab.json</c>, <c>merges.txt</c> and
/// <c>tokenizer_config.json</c>. A checkpoint that also ships <c>tokenizer.model</c> or <c>tokenizer.json</c>
/// takes a different road through the engine's converter and is refused here by name rather than converted
/// along this one.
/// </para>
/// </remarks>
internal static class Gpt2BpeTokenizerExport
{
    /// <summary>The tokenizer model name this export writes.</summary>
    internal const string TokenizerModel = "gpt2";

    /// <summary>The pre-tokenizer this export writes.</summary>
    internal const string PreTokenizer = "gpt-2";

    private const string SentencePieceSpace = "▁";

    /// <summary>
    /// The public parameter the supplied token names arrive through, which is what an
    /// <see cref="ArgumentException"/> about one of them has to name.
    /// </summary>
    private const string OptionsParameterName = "options";

    /// <summary>Reads a checkpoint's GPT-2 tokenizer files and builds the vocabulary to write.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="config">The checkpoint's configuration, which supplies the vocabulary size.</param>
    /// <param name="tokenizerConfig">The parsed <c>tokenizer_config.json</c>, or <see langword="null"/>.</param>
    /// <param name="addedSpecialTokens">
    /// Token contents the caller says are added and special, or <see langword="null"/> for none.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The vocabulary, the merges, the special tokens and the chat template.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="addedSpecialTokens"/> names a token the vocabulary does not hold.
    /// </exception>
    internal static async Task<TokenizerExport> LoadAsync(string directory, HuggingFaceConfig config,
        TokenizerConfig tokenizerConfig, IReadOnlyList<string> addedSpecialTokens,
        CancellationToken cancellationToken)
    {
        RequireGpt2FileSet(directory, tokenizerConfig);

        Dictionary<long, string> reverseVocabulary = await ReadVocabularyAsync(directory, cancellationToken)
            .ConfigureAwait(false);
        long vocabularySize = GetVocabularySize(config, reverseVocabulary);
        HashSet<string> supplied = ReadSuppliedTokens(addedSpecialTokens, reverseVocabulary);

        var export = new TokenizerExport
        {
            Model = TokenizerModel,
            Pre = PreTokenizer,
            AddSpacePrefix = tokenizerConfig == null ? null : tokenizerConfig.AddPrefixSpace,
        };

        for (long id = 0; id < vocabularySize; id++)
        {
            if (!reverseVocabulary.TryGetValue(id, out string token))
            {
                export.Tokens.Add("[PAD" + id.ToString(CultureInfo.InvariantCulture) + "]");
                export.TokenTypes.Add((int)GgufTokenType.Unused);
                continue;
            }

            export.TokenTypes.Add((int)ClassifyToken(tokenizerConfig, supplied, id, ref token));
            export.Tokens.Add(token);
        }

        SpecialVocabulary special = await SpecialVocabulary
            .LoadAsync(directory, tokenizerConfig, config, true, cancellationToken).ConfigureAwait(false);
        export.Merges.AddRange(special.Merges);
        foreach (KeyValuePair<string, long> entry in special.SpecialTokenIds)
        {
            export.SpecialTokenIds[entry.Key] = entry.Value;
        }

        foreach (KeyValuePair<string, bool> entry in special.AddSpecialTokens)
        {
            export.AddSpecialTokens[entry.Key] = entry.Value;
        }

        export.ChatTemplate = special.ChatTemplate;
        return export;
    }

    /// <summary>Whether a token's text looks like a control token even when nothing says it is one.</summary>
    /// <param name="token">The token's text.</param>
    /// <returns><see langword="true"/> when it looks special.</returns>
    internal static bool DoesTokenLookSpecial(string token)
    {
        if (string.Equals(token, "<pad>", StringComparison.Ordinal)
            || string.Equals(token, "<mask>", StringComparison.Ordinal)
            || string.Equals(token, "<2mass>", StringComparison.Ordinal)
            || string.Equals(token, "[@BOS@]", StringComparison.Ordinal))
        {
            return true;
        }

        if (token.StartsWith("<|", StringComparison.Ordinal) && token.EndsWith("|>", StringComparison.Ordinal))
        {
            return true;
        }

        if (token.StartsWith("<｜", StringComparison.Ordinal)
            && token.EndsWith("｜>", StringComparison.Ordinal))
        {
            return true;
        }

        return token.StartsWith("<unused", StringComparison.Ordinal)
            && token.EndsWith(">", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks the token contents the caller supplied against the vocabulary and collects them.
    /// </summary>
    /// <param name="named">The contents the caller named, or <see langword="null"/>.</param>
    /// <param name="reverseVocabulary">The vocabulary, keyed by identifier.</param>
    /// <returns>The contents, as a set; empty when the caller named none.</returns>
    /// <exception cref="ArgumentException">A named token is empty, or is not in the vocabulary.</exception>
    private static HashSet<string> ReadSuppliedTokens(IReadOnlyList<string> named,
        Dictionary<long, string> reverseVocabulary)
    {
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        if (named == null || named.Count == 0)
        {
            return supplied;
        }

        var contents = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<long, string> entry in reverseVocabulary)
        {
            contents.Add(entry.Value);
        }

        for (int i = 0; i < named.Count; i++)
        {
            string token = named[i];
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("ConvertOptions.AddedSpecialTokens holds an empty entry at " +
                    "position " + i.ToString(CultureInfo.InvariantCulture) + ". Every entry names a token of " +
                    "the checkpoint's vocabulary.", OptionsParameterName);
            }

            if (!contents.Contains(token))
            {
                throw new ArgumentException("ConvertOptions.AddedSpecialTokens names the token \"" + token +
                    "\", which this checkpoint's vocabulary does not hold.", OptionsParameterName);
            }

            supplied.Add(token);
        }

        return supplied;
    }

    private static GgufTokenType ClassifyToken(TokenizerConfig tokenizerConfig, HashSet<string> supplied,
        long id, ref string token)
    {
        // A token the caller named is added and special by the caller's word, which is the one thing a
        // conversion that reads files cannot learn for itself. Everything after this line is the engine's
        // rule, applied to that answer exactly as it is applied to an answer read out of a file.
        bool added = supplied.Contains(token);
        bool special = added;
        if (!added && tokenizerConfig != null)
        {
            if (tokenizerConfig.AddedTokens.TryGetValue(id, out AddedToken entry)
                && string.Equals(entry.Content, token, StringComparison.Ordinal))
            {
                added = true;
                special = entry.Special;
            }
            else if (tokenizerConfig.IsDeclaredSpecialToken(token))
            {
                added = true;
                special = true;
            }
        }

        if (!added)
        {
            return GgufTokenType.Normal;
        }

        if (special || DoesTokenLookSpecial(token))
        {
            return GgufTokenType.Control;
        }

        token = token.Replace(SentencePieceSpace, " ");
        return GgufTokenType.UserDefined;
    }

    private static void RequireGpt2FileSet(string directory, TokenizerConfig tokenizerConfig)
    {
        if (File.Exists(Path.Combine(directory, "tokenizer.model")))
        {
            throw new NotSupportedException("The checkpoint ships a tokenizer.model, so its tokenizer is a " +
                "SentencePiece model. This version converts GPT-2 byte-level BPE tokenizers.");
        }

        if (File.Exists(Path.Combine(directory, "tokenizer.json")))
        {
            throw new NotSupportedException("The checkpoint ships a tokenizer.json. This version reads the " +
                "GPT-2 byte-level BPE file set - vocab.json and merges.txt - which is the set the checkpoints " +
                "it targets ship.");
        }

        if (!File.Exists(Path.Combine(directory, "vocab.json")))
        {
            string named = tokenizerConfig == null || tokenizerConfig.TokenizerClass == null
                ? "no tokenizer class"
                : "the tokenizer class \"" + tokenizerConfig.TokenizerClass + "\"";
            throw new NotSupportedException("The checkpoint has " + named + " and no vocab.json, so this " +
                "version cannot read its tokenizer.");
        }

        if (!File.Exists(Path.Combine(directory, "merges.txt")))
        {
            throw new NotSupportedException("The checkpoint has a vocab.json but no merges.txt, so its " +
                "tokenizer is not a GPT-2 byte-level BPE this version can convert.");
        }
    }

    private static async Task<Dictionary<long, string>> ReadVocabularyAsync(string directory,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "vocab.json");
        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var reverse = new Dictionary<long, string>();
        try
        {
            using (JsonDocument document = JsonDocument.Parse(content))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new CheckpointFormatException("The file \"" + path + "\" is not a JSON object.");
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetInt64(out long id))
                    {
                        throw new CheckpointFormatException("The file \"" + path + "\" maps the token \"" +
                            property.Name + "\" on to something that is not an identifier.");
                    }

                    reverse[id] = property.Name;
                }
            }
        }
        catch (JsonException exception)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" is not valid JSON.", exception);
        }

        if (reverse.Count == 0)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" holds no tokens.");
        }

        return reverse;
    }

    private static long GetVocabularySize(HuggingFaceConfig config, Dictionary<long, string> reverseVocabulary)
    {
        long highest = -1;
        foreach (long id in reverseVocabulary.Keys)
        {
            if (id > highest)
            {
                highest = id;
            }
        }

        long size = config.TryGetInt64(new[] { "vocab_size" }, out long configured)
            ? configured
            : reverseVocabulary.Count;
        if (highest >= size)
        {
            throw new CheckpointFormatException("The checkpoint's vocabulary holds the identifier " + highest +
                ", which is outside the vocabulary size " + size + " that config.json declares.");
        }

        return size;
    }
}
