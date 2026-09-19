using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221, gguf-py/gguf/vocab.py@b10221;

/// <summary>
/// Turns a SentencePiece tokenizer - a <c>tokenizer.model</c>, and the two added-token files a publisher may
/// ship beside it - into the vocabulary a GGUF file carries.
/// </summary>
/// <remarks>
/// <para>
/// This vocabulary carries SCORES as well as tokens and types, which the GPT-2 byte-level route does not, and
/// it carries no merge table at all: the engine re-derives the segmentation from the scores. The tokenizer
/// model written into the file is <c>llama</c> and the pre-tokenizer is <c>default</c>, which are fixed strings
/// rather than anything read from the checkpoint - there is no fingerprint to look up on this road.
/// </para>
/// <para>
/// Three passes build the vocabulary and the order of them is what decides the result. Every identifier below
/// the configured vocabulary size starts as an unused <c>[PAD&lt;n&gt;]</c> placeholder scoring -10000; the
/// pieces of the model then fill the identifiers they occupy, with the kind the model gives each piece;
/// <c>added_tokens.json</c> then overwrites what it names as user-defined, scoring -1000; and
/// <c>tokenizer_config.json</c>'s added-token table overwrites last, marking an entry control when it is
/// special or merely looks special and user-defined otherwise. An identifier at or above the vocabulary size is
/// ignored wherever it is named, rather than growing the vocabulary past the size the model's embedding matrix
/// has rows for.
/// </para>
/// <para>
/// A piece the model itself calls user-defined is NOT written as a user-defined token. The engine maps only
/// unknown, control, unused and byte pieces on to their own kinds and writes everything else as normal, so a
/// piece is user-defined in the output only when one of the two added-token files says so. That asymmetry is
/// the engine's and is reproduced here deliberately.
/// </para>
/// </remarks>
internal static class SentencePieceTokenizerExport
{
    /// <summary>The tokenizer model name this export writes.</summary>
    internal const string TokenizerModel = "llama";

    /// <summary>The pre-tokenizer this export writes. It is a constant on this road, not a fingerprint.</summary>
    internal const string PreTokenizer = "default";

    /// <summary>The score every placeholder of the unfilled vocabulary carries.</summary>
    internal const float PlaceholderScore = -10000f;

    /// <summary>The score every added token carries, whichever file added it.</summary>
    internal const float AddedTokenScore = -1000f;

    /// <summary>The file a publisher lists tokens they added beside the trained model in.</summary>
    internal const string AddedTokensFileName = "added_tokens.json";

    private const string SentencePieceSpace = "▁";

    /// <summary>Reads a checkpoint's SentencePiece tokenizer and builds the vocabulary to write.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="config">The checkpoint's configuration, which supplies the vocabulary size.</param>
    /// <param name="tokenizerConfig">The parsed <c>tokenizer_config.json</c>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The vocabulary, the scores, the special tokens and the chat template.</returns>
    /// <exception cref="CheckpointFormatException">A tokenizer file cannot be read.</exception>
    internal static async Task<TokenizerExport> LoadAsync(string directory, HuggingFaceConfig config,
        TokenizerConfig tokenizerConfig, CancellationToken cancellationToken)
    {
        SentencePieceModel model = await SentencePieceModel.LoadAsync(directory, cancellationToken)
            .ConfigureAwait(false);
        long vocabularySize = GetVocabularySize(config, model);

        var tokens = new List<string>((int)vocabularySize);
        var scores = new List<float>((int)vocabularySize);
        var types = new List<int>((int)vocabularySize);
        for (long id = 0; id < vocabularySize; id++)
        {
            tokens.Add("[PAD" + id.ToString(CultureInfo.InvariantCulture) + "]");
            scores.Add(PlaceholderScore);
            types.Add((int)GgufTokenType.Unused);
        }

        for (int id = 0; id < model.Pieces.Count && id < vocabularySize; id++)
        {
            SentencePiecePiece piece = model.Pieces[id];
            tokens[id] = piece.Text;
            scores[id] = piece.Score;
            types[id] = (int)ToGgufTokenType(piece.Type);
        }

        await ApplyAddedTokensFileAsync(directory, vocabularySize, tokens, scores, types, cancellationToken)
            .ConfigureAwait(false);
        ApplyAddedTokenTable(tokenizerConfig, vocabularySize, tokens, scores, types);

        var export = new TokenizerExport
        {
            Model = TokenizerModel,
            Pre = PreTokenizer,

            //Written before the tokenizer model is, on both roads: see VocabularyExport.
            AddSpacePrefix = tokenizerConfig == null ? null : tokenizerConfig.AddPrefixSpace,
        };
        export.Tokens.AddRange(tokens);
        export.Scores.AddRange(scores);
        export.TokenTypes.AddRange(types);

        // The engine reads the special vocabulary with the vocabulary size in hand on this road, so an
        // identifier a configuration names that is not in the vocabulary is left out rather than written.
        SpecialVocabulary special = await SpecialVocabulary
            .LoadAsync(directory, tokenizerConfig, config, false, vocabularySize, cancellationToken)
            .ConfigureAwait(false);
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

    /// <summary>
    /// What a piece of a SentencePiece model becomes in a GGUF vocabulary.
    /// </summary>
    /// <param name="type">The kind the model gives the piece.</param>
    /// <returns>The kind to write.</returns>
    /// <remarks>
    /// Unknown, control, unused and byte pieces keep their kind; everything else - including a piece the model
    /// calls user-defined - is written as a normal token, because that is what the engine's converter does.
    /// </remarks>
    internal static GgufTokenType ToGgufTokenType(SentencePieceTokenType type)
    {
        switch (type)
        {
            case SentencePieceTokenType.Unknown:
                return GgufTokenType.Unknown;
            case SentencePieceTokenType.Control:
                return GgufTokenType.Control;
            case SentencePieceTokenType.Unused:
                return GgufTokenType.Unused;
            case SentencePieceTokenType.Byte:
                return GgufTokenType.Byte;
            default:
                return GgufTokenType.Normal;
        }
    }

    /// <summary>
    /// The number of identifiers the vocabulary written into the file has.
    /// </summary>
    /// <param name="config">The checkpoint's configuration.</param>
    /// <param name="model">The tokenizer model.</param>
    /// <returns>The vocabulary size.</returns>
    /// <exception cref="CheckpointFormatException">The configuration declares a size no vocabulary can have.</exception>
    private static long GetVocabularySize(HuggingFaceConfig config, SentencePieceModel model)
    {
        // The engine looks for the per-layer input size first, which one architecture outside this version's
        // reach carries, then the ordinary one, and falls back to the model's own count when neither is a
        // positive number.
        if (config.TryGetInt64(new[] { "vocab_size_per_layer_input", "vocab_size" }, out long configured)
            && configured > 0)
        {
            if (configured > int.MaxValue)
            {
                throw new CheckpointFormatException("The checkpoint's config.json declares a vocabulary of " +
                    configured.ToString(CultureInfo.InvariantCulture) + " tokens.");
            }

            return configured;
        }

        return model.Pieces.Count;
    }

    private static async Task ApplyAddedTokensFileAsync(string directory, long vocabularySize,
        List<string> tokens, List<float> scores, List<int> types, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, AddedTokensFileName);
        if (!File.Exists(path))
        {
            return;
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            using (JsonDocument document = JsonDocument.Parse(content))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new CheckpointFormatException("The file \"" + path + "\" is not a JSON object.");
                }

                foreach (JsonProperty entry in document.RootElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Number
                        || !entry.Value.TryGetInt64(out long id))
                    {
                        throw new CheckpointFormatException("The file \"" + path + "\" maps the token \"" +
                            entry.Name + "\" on to something that is not an identifier.");
                    }

                    if (id < 0 || id >= vocabularySize)
                    {
                        // The engine warns and moves on; an identifier outside the vocabulary names no row of
                        // the embedding matrix, so there is nothing it could mean.
                        continue;
                    }

                    tokens[(int)id] = entry.Name;
                    scores[(int)id] = AddedTokenScore;
                    types[(int)id] = (int)GgufTokenType.UserDefined;
                }
            }
        }
        catch (JsonException exception)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" is not valid JSON.", exception);
        }
    }

    private static void ApplyAddedTokenTable(TokenizerConfig tokenizerConfig, long vocabularySize,
        List<string> tokens, List<float> scores, List<int> types)
    {
        if (tokenizerConfig == null)
        {
            return;
        }

        foreach (KeyValuePair<long, AddedToken> entry in tokenizerConfig.AddedTokens)
        {
            long id = entry.Key;
            if (id < 0 || id >= vocabularySize)
            {
                continue;
            }

            string token = entry.Value.Content;
            if (entry.Value.Special || Gpt2BpeTokenizerExport.DoesTokenLookSpecial(token))
            {
                types[(int)id] = (int)GgufTokenType.Control;
            }
            else
            {
                token = token.Replace(SentencePieceSpace, " ");
                types[(int)id] = (int)GgufTokenType.UserDefined;
            }

            scores[(int)id] = AddedTokenScore;
            tokens[(int)id] = token;
        }
    }
}
