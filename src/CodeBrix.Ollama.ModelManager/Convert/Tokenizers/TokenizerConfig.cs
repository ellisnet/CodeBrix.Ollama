using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A checkpoint's <c>tokenizer_config.json</c>, read for the handful of things a conversion needs: the
/// tokenizer class, the added-token table, the declared special tokens, the <c>add_&lt;kind&gt;_token</c> flags,
/// the chat template and whether a space is put in front of the text.
/// </summary>
/// <remarks>
/// What is NOT here is as important as what is. A tokenizer class carries defaults of its own - the names of its
/// unknown, beginning-of-sequence, end-of-sequence and padding tokens - and those defaults live in the
/// publisher's Python code, not in this file. A conversion reads files; it never runs a publisher's code, so a
/// token that only that code declares is read as an ordinary token.
/// </remarks>
internal sealed class TokenizerConfig
{
    private readonly Dictionary<long, AddedToken> _addedTokens;
    private readonly Dictionary<string, string> _specialTokenContents;
    private readonly Dictionary<string, bool> _addSpecialTokens;

    private TokenizerConfig(Dictionary<long, AddedToken> addedTokens,
        Dictionary<string, string> specialTokenContents, Dictionary<string, bool> addSpecialTokens,
        string tokenizerClass, string chatTemplate, bool? addPrefixSpace)
    {
        _addedTokens = addedTokens;
        _specialTokenContents = specialTokenContents;
        _addSpecialTokens = addSpecialTokens;
        TokenizerClass = tokenizerClass;
        ChatTemplate = chatTemplate;
        AddPrefixSpace = addPrefixSpace;
    }

    /// <summary>The class the publisher says the tokenizer is, or <see langword="null"/>.</summary>
    internal string TokenizerClass { get; }

    /// <summary>The chat template, or <see langword="null"/>.</summary>
    internal string ChatTemplate { get; }

    /// <summary>Whether a space is put in front of the text, when the file says so.</summary>
    internal bool? AddPrefixSpace { get; }

    /// <summary>The added-token table, keyed by identifier.</summary>
    internal IReadOnlyDictionary<long, AddedToken> AddedTokens
    {
        get { return _addedTokens; }
    }

    /// <summary>Reads the tokenizer configuration in a checkpoint directory, if there is one.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The configuration, or <see langword="null"/> when the directory has none.</returns>
    internal static async Task<TokenizerConfig> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "tokenizer_config.json");
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(content, path);
    }

    /// <summary>Reads a tokenizer configuration from the bytes of a <c>tokenizer_config.json</c>.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <param name="path">The path, for messages.</param>
    /// <returns>The configuration.</returns>
    internal static TokenizerConfig Parse(byte[] content, string path)
    {
        var addedTokens = new Dictionary<long, AddedToken>();
        var specialTokenContents = new Dictionary<string, string>(StringComparer.Ordinal);
        var addSpecialTokens = new Dictionary<string, bool>(StringComparer.Ordinal);
        string tokenizerClass = null;
        string chatTemplate = null;
        bool? addPrefixSpace = null;

        try
        {
            using (JsonDocument document = JsonDocument.Parse(content))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new CheckpointFormatException("The file \"" + path + "\" is not a JSON object.");
                }

                JsonElement root = document.RootElement;
                if (root.TryGetProperty("tokenizer_class", out JsonElement classElement)
                    && classElement.ValueKind == JsonValueKind.String)
                {
                    tokenizerClass = classElement.GetString();
                }

                if (root.TryGetProperty("chat_template", out JsonElement templateElement)
                    && templateElement.ValueKind == JsonValueKind.String)
                {
                    chatTemplate = templateElement.GetString();
                }

                if (root.TryGetProperty("add_prefix_space", out JsonElement prefixElement)
                    && (prefixElement.ValueKind == JsonValueKind.True
                        || prefixElement.ValueKind == JsonValueKind.False))
                {
                    addPrefixSpace = prefixElement.ValueKind == JsonValueKind.True;
                }

                if (root.TryGetProperty("added_tokens_decoder", out JsonElement decoderElement)
                    && decoderElement.ValueKind == JsonValueKind.Object)
                {
                    ReadAddedTokens(decoderElement, addedTokens);
                }

                for (int i = 0; i < SpecialVocabulary.SpecialTokenKinds.Length; i++)
                {
                    string kind = SpecialVocabulary.SpecialTokenKinds[i];
                    if (root.TryGetProperty("add_" + kind + "_token", out JsonElement addElement)
                        && (addElement.ValueKind == JsonValueKind.True
                            || addElement.ValueKind == JsonValueKind.False))
                    {
                        addSpecialTokens[kind] = addElement.ValueKind == JsonValueKind.True;
                    }

                    if (!root.TryGetProperty(kind + "_token", out JsonElement tokenElement))
                    {
                        continue;
                    }

                    string tokenContent = ReadTokenContent(tokenElement);
                    if (tokenContent != null)
                    {
                        specialTokenContents[kind] = tokenContent;
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" is not valid JSON.", exception);
        }

        return new TokenizerConfig(addedTokens, specialTokenContents, addSpecialTokens, tokenizerClass,
            chatTemplate, addPrefixSpace);
    }

    /// <summary>Gets the <c>add_&lt;kind&gt;_token</c> flag for one kind of special token.</summary>
    /// <param name="kind">The kind, for example <c>bos</c>.</param>
    /// <param name="value">Receives the flag.</param>
    /// <returns><see langword="true"/> when the file declares the flag.</returns>
    internal bool TryGetAddSpecialToken(string kind, out bool value)
    {
        return _addSpecialTokens.TryGetValue(kind, out value);
    }

    /// <summary>Whether a token's text is one the file declares as a special token of some kind.</summary>
    /// <param name="content">The token's text.</param>
    /// <returns><see langword="true"/> when the file declares it.</returns>
    internal bool IsDeclaredSpecialToken(string content)
    {
        foreach (KeyValuePair<string, string> entry in _specialTokenContents)
        {
            if (string.Equals(entry.Value, content, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReadAddedTokens(JsonElement element, Dictionary<long, AddedToken> addedTokens)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!long.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string content = property.Value.TryGetProperty("content", out JsonElement contentElement)
                && contentElement.ValueKind == JsonValueKind.String
                    ? contentElement.GetString()
                    : null;
            if (content == null)
            {
                continue;
            }

            bool special = property.Value.TryGetProperty("special", out JsonElement specialElement)
                && specialElement.ValueKind == JsonValueKind.True;
            bool normalized = property.Value.TryGetProperty("normalized", out JsonElement normalizedElement)
                && normalizedElement.ValueKind == JsonValueKind.True;
            addedTokens[id] = new AddedToken(content, special, normalized);
        }
    }

    private static string ReadTokenContent(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("content", out JsonElement contentElement)
            && contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString();
        }

        return null;
    }
}
