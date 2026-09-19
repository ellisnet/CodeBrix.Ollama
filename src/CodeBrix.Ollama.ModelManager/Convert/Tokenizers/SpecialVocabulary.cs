using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelManager; //was previously: gguf-py/gguf/vocab.py@b10221;

/// <summary>
/// The special tokens, merge table and chat template a checkpoint's tokenizer files carry, found the way the
/// inference engine's converter finds them.
/// </summary>
/// <remarks>
/// <para>
/// The lookup order is the part that matters and the part that is easy to get wrong. The identifiers of the
/// special tokens come from <c>config.json</c>'s <c>&lt;kind&gt;_token_id</c> entries, NOT from
/// <c>tokenizer_config.json</c> - a checkpoint whose tokenizer configuration names a token but whose model
/// configuration does not give it an identifier writes no identifier at all. The <c>add_&lt;kind&gt;_token</c>
/// flags and the chat template do come from <c>tokenizer_config.json</c>, and only when they are there: the
/// tokenizer class's own defaults are never consulted.
/// </para>
/// <para>
/// The merge table is read from <c>merges.txt</c>, and the first line is dropped ONLY when it starts with a
/// <c>#</c>. Several publishers' own tokenizers drop the first line unconditionally and so use one merge fewer;
/// what is written here is what the engine reads, because it is the engine that will run the model.
/// </para>
/// </remarks>
internal sealed class SpecialVocabulary
{
    /// <summary>The kinds of special token that are looked for, in the order they are looked for.</summary>
    internal static readonly string[] SpecialTokenKinds = { "bos", "eos", "unk", "sep", "pad", "cls", "mask" };

    private readonly long? _vocabularySize;

    private SpecialVocabulary(long? vocabularySize)
    {
        _vocabularySize = vocabularySize;
    }

    /// <summary>The merge table, in rank order.</summary>
    internal List<string> Merges { get; } = new List<string>();

    /// <summary>The identifiers found, keyed by kind, in the order they were found.</summary>
    internal OrderedDictionary<string, long> SpecialTokenIds { get; } =
        new OrderedDictionary<string, long>(StringComparer.Ordinal);

    /// <summary>The <c>add_&lt;kind&gt;_token</c> flags found, in the order they were found.</summary>
    internal OrderedDictionary<string, bool> AddSpecialTokens { get; } =
        new OrderedDictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>The chat template, when there is one.</summary>
    internal string ChatTemplate { get; private set; }

    /// <summary>Reads the special vocabulary out of a checkpoint directory.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="tokenizerConfig">The parsed <c>tokenizer_config.json</c>, or <see langword="null"/>.</param>
    /// <param name="config">The parsed <c>config.json</c>.</param>
    /// <param name="loadMerges">Whether to read <c>merges.txt</c>.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The special vocabulary.</returns>
    internal static Task<SpecialVocabulary> LoadAsync(string directory, TokenizerConfig tokenizerConfig,
        HuggingFaceConfig config, bool loadMerges, CancellationToken cancellationToken)
    {
        return LoadAsync(directory, tokenizerConfig, config, loadMerges, null, cancellationToken);
    }

    /// <summary>Reads the special vocabulary out of a checkpoint directory, with the vocabulary size known.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="tokenizerConfig">The parsed <c>tokenizer_config.json</c>, or <see langword="null"/>.</param>
    /// <param name="config">The parsed <c>config.json</c>.</param>
    /// <param name="loadMerges">Whether to read <c>merges.txt</c>.</param>
    /// <param name="vocabularySize">
    /// How many identifiers the vocabulary has, so that one outside it is left out; <see langword="null"/> when
    /// the caller does not know, which is the road the byte-level route takes.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The special vocabulary.</returns>
    internal static async Task<SpecialVocabulary> LoadAsync(string directory, TokenizerConfig tokenizerConfig,
        HuggingFaceConfig config, bool loadMerges, long? vocabularySize,
        CancellationToken cancellationToken)
    {
        var vocabulary = new SpecialVocabulary(vocabularySize);
        vocabulary.ApplyTokenizerConfig(directory, tokenizerConfig, cancellationToken);
        vocabulary.ApplyModelConfig(config);
        if (loadMerges && vocabulary.Merges.Count == 0)
        {
            await vocabulary.LoadMergesAsync(directory, cancellationToken).ConfigureAwait(false);
        }

        return vocabulary;
    }

    private void ApplyTokenizerConfig(string directory, TokenizerConfig tokenizerConfig,
        CancellationToken cancellationToken)
    {
        if (tokenizerConfig == null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ChatTemplate = ReadChatTemplate(directory, tokenizerConfig);
        for (int i = 0; i < SpecialTokenKinds.Length; i++)
        {
            string kind = SpecialTokenKinds[i];
            if (tokenizerConfig.TryGetAddSpecialToken(kind, out bool add))
            {
                AddSpecialTokens[kind] = add;
            }

            // The identifier a tokenizer configuration names is resolved through the added-token table of a
            // tokenizer.json, which a checkpoint on this path does not have; the identifier therefore comes
            // from config.json alone. This is deliberate, not an omission.
        }
    }

    private static string ReadChatTemplate(string directory, TokenizerConfig tokenizerConfig)
    {
        string jinja = Path.Combine(directory, "chat_template.jinja");
        if (File.Exists(jinja))
        {
            return File.ReadAllText(jinja, Encoding.UTF8);
        }

        string json = Path.Combine(directory, "chat_template.json");
        if (File.Exists(json))
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(json)))
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("chat_template", out JsonElement template)
                    && template.ValueKind == JsonValueKind.String)
                {
                    return template.GetString();
                }
            }
        }

        return tokenizerConfig.ChatTemplate;
    }

    private void ApplyModelConfig(HuggingFaceConfig config)
    {
        for (int i = 0; i < SpecialTokenKinds.Length; i++)
        {
            string kind = SpecialTokenKinds[i];
            if (config.TryGetInt64(new[] { kind + "_token_id" }, out long id))
            {
                SetSpecialToken(kind, id);
            }
        }
    }

    private void SetSpecialToken(string kind, long id)
    {
        if (id < 0)
        {
            throw new CheckpointFormatException("The checkpoint's config.json gives the " + kind +
                " token the identifier " + id + ".");
        }

        if (_vocabularySize.HasValue && id >= _vocabularySize.Value)
        {
            // The engine warns and skips an identifier the vocabulary cannot hold rather than writing it.
            return;
        }

        if (!SpecialTokenIds.ContainsKey(kind))
        {
            SpecialTokenIds[kind] = id;
        }
    }

    private async Task LoadMergesAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "merges.txt");
        if (!File.Exists(path))
        {
            return;
        }

        string[] lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        Merges.AddRange(Gpt2MergeTable.Parse(lines));
    }
}
