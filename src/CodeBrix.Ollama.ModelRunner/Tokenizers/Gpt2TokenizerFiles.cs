using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Builds a byte-level byte-pair encoder out of the tokenizer files a bundle carries, and refuses by name a
/// tokenizer of a kind this library does not read.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT LOOKS FOR: <c>vocab.json</c> and <c>merges.txt</c>, which together are a byte-level
/// byte-pair encoder; and, when they are there, <c>tokenizer_config.json</c>, <c>special_tokens_map.json</c>
/// and <c>added_tokens.json</c>, which say whether a beginning-of-sequence token is added, whether a space is
/// put in front of the text, and which tokens are matched as text.
/// </para>
/// <para>
/// WHAT IT REFUSES, by the name of the thing it found: a bundle carrying a SentencePiece model
/// (<c>tokenizer.model</c>) and no byte-level pair; a bundle carrying only a <c>tokenizer.json</c>, which is a
/// different file format again; and a bundle carrying no tokenizer at all. Guessing at any of those produces
/// a model that runs and writes nonsense, which is the one outcome worth refusing for.
/// </para>
/// <para>
/// THE MERGE TABLE IS READ BY <c>CodeBrix.Ollama.Core</c>'s primitive, which is the same reading the
/// checkpoint conversion on the other side of this repository uses: a first line is dropped only when it
/// begins with <c>#</c>. A bundle written by a model builder carries that header - the publisher's own save
/// path writes one - so what is read here is the same table, entry for entry, that the publisher's own
/// tokenizer held.
/// </para>
/// </remarks>
internal static class Gpt2TokenizerFiles
{
    /// <summary>The vocabulary file of a byte-level byte-pair encoder.</summary>
    internal const string VocabularyFileName = "vocab.json";

    /// <summary>The merge file of a byte-level byte-pair encoder.</summary>
    internal const string MergesFileName = "merges.txt";

    /// <summary>The name this library reports for the kind of tokenizer it reads.</summary>
    internal const string Kind = "GPT-2 byte-level BPE";

    /// <summary>Builds the tokenizer a bundle's files describe.</summary>
    /// <param name="files">The bundle's files: the publisher's name for each one against its real path.</param>
    /// <returns>The tokenizer.</returns>
    /// <exception cref="ModelLoadException">
    /// The files hold no byte-level pair, hold a tokenizer of another kind, or hold one that cannot be read.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The bundle declares an added token with a matching rule this library does not implement.
    /// </exception>
    internal static Gpt2ByteLevelTokenizer Load(IReadOnlyDictionary<string, string> files)
    {
        string vocabulary = Find(files, VocabularyFileName);
        string merges = Find(files, MergesFileName);

        if (vocabulary == null || merges == null)
        {
            Refuse(files, vocabulary, merges);
        }

        Dictionary<string, int> encoder = ReadVocabulary(vocabulary);
        List<string> table = Gpt2MergeTable.Parse(File.ReadAllLines(merges));
        Gpt2TokenizerSettings settings = ReadSettings(files, encoder);

        return new Gpt2ByteLevelTokenizer(encoder, table, settings);
    }

    private static void Refuse(
        IReadOnlyDictionary<string, string> files, string vocabulary, string merges)
    {
        if (Find(files, "tokenizer.model") != null)
        {
            throw new ModelLoadException(
                "This bundle's tokenizer is a SentencePiece model (tokenizer.model), and this driver reads a "
                + Kind + " from vocab.json and merges.txt.");
        }

        if (Find(files, "tokenizer.json") != null && vocabulary == null)
        {
            throw new ModelLoadException(
                "This bundle's tokenizer is a tokenizer.json, and this driver reads a " + Kind
                + " from vocab.json and merges.txt.");
        }

        throw new ModelLoadException(
            "This bundle carries no tokenizer this driver can read: it is missing "
            + (vocabulary == null ? VocabularyFileName : MergesFileName) + ".");
    }

    private static Dictionary<string, int> ReadVocabulary(string path)
    {
        if (!File.Exists(path))
        {
            throw new ModelLoadException("There is no file at '" + path + "'.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException exception)
        {
            throw new ModelLoadException(
                "The bundle's " + VocabularyFileName + " is not valid JSON.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ModelLoadException(
                    "The bundle's " + VocabularyFileName + " is not a set of token-to-number pairs.");
            }

            Dictionary<string, int> encoder = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                if (!entry.Value.TryGetInt32(out int number)) continue;
                encoder[entry.Name] = number;
            }

            return encoder;
        }
    }

    private static Gpt2TokenizerSettings ReadSettings(
        IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, int> encoder)
    {
        bool addBeginningOfSequence = false;
        bool addPrefixSpace = false;
        string beginningOfSequence = null;
        string unknown = null;
        Dictionary<string, Gpt2AddedToken> added = new Dictionary<string, Gpt2AddedToken>(StringComparer.Ordinal);

        string configuration = Find(files, "tokenizer_config.json");
        if (configuration != null && File.Exists(configuration))
        {
            using JsonDocument document = Parse(configuration, "tokenizer_config.json");
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                addBeginningOfSequence = Flag(root, "add_bos_token");
                addPrefixSpace = Flag(root, "add_prefix_space");
                beginningOfSequence = TokenText(root, "bos_token");
                unknown = TokenText(root, "unk_token");
                ReadAddedTokensDecoder(root, added);
            }
        }

        string map = Find(files, "special_tokens_map.json");
        if (map != null && File.Exists(map))
        {
            using JsonDocument document = Parse(map, "special_tokens_map.json");
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                beginningOfSequence ??= TokenText(root, "bos_token");
                unknown ??= TokenText(root, "unk_token");
                foreach (string name in new[] { "bos_token", "eos_token", "pad_token", "unk_token" })
                {
                    Add(added, TokenText(root, name), encoder, true);
                }
            }
        }

        string extra = Find(files, "added_tokens.json");
        if (extra != null && File.Exists(extra))
        {
            using JsonDocument document = Parse(extra, "added_tokens.json");
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in document.RootElement.EnumerateObject())
                {
                    if (!entry.Value.TryGetInt32(out int number)) continue;
                    added[entry.Name] = new Gpt2AddedToken(entry.Name, number, false);
                }
            }
        }

        List<Gpt2AddedToken> tokens = new List<Gpt2AddedToken>(added.Values);
        return new Gpt2TokenizerSettings(
            addBeginningOfSequence, addPrefixSpace, beginningOfSequence, unknown, tokens);
    }

    private static void ReadAddedTokensDecoder(JsonElement root, Dictionary<string, Gpt2AddedToken> added)
    {
        if (!root.TryGetProperty("added_tokens_decoder", out JsonElement decoder)
            || decoder.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty entry in decoder.EnumerateObject())
        {
            if (!int.TryParse(
                    entry.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Object) continue;

            string content = Text(entry.Value, "content");
            if (string.IsNullOrEmpty(content)) continue;

            //lstrip, rstrip and single_word each change WHERE a token matches, and none of them is
            //implemented here; a bundle that asks for one is refused by name rather than tokenized a
            //different way from the way it was trained.
            foreach (string rule in new[] { "lstrip", "rstrip", "single_word" })
            {
                if (!Flag(entry.Value, rule)) continue;

                throw new NotSupportedException(
                    "The bundle's token '" + content + "' asks for '" + rule + "', and this library's "
                    + Kind + " tokenizer matches an added token as plain text without it.");
            }

            added[content] = new Gpt2AddedToken(content, number, Flag(entry.Value, "special"));
        }
    }

    private static void Add(
        Dictionary<string, Gpt2AddedToken> added,
        string content,
        IReadOnlyDictionary<string, int> encoder,
        bool isSpecial)
    {
        if (string.IsNullOrEmpty(content) || added.ContainsKey(content)) return;
        if (!encoder.TryGetValue(content, out int number)) return;

        added[content] = new Gpt2AddedToken(content, number, isSpecial);
    }

    private static JsonDocument Parse(string path, string name)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException exception)
        {
            throw new ModelLoadException("The bundle's " + name + " is not valid JSON.", exception);
        }
    }

    private static string TokenText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element)) return null;
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        if (element.ValueKind == JsonValueKind.Object) return Text(element, "content");
        return null;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string Find(IReadOnlyDictionary<string, string> files, string name)
    {
        foreach (KeyValuePair<string, string> file in files)
        {
            int slash = file.Key.LastIndexOfAny(new[] { '/', '\\' });
            string leaf = slash < 0 ? file.Key : file.Key.Substring(slash + 1);
            if (string.Equals(leaf, name, StringComparison.Ordinal)) return file.Value;
        }

        return null;
    }
}
