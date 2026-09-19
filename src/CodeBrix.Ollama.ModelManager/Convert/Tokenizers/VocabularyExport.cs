using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/llama.py@b10221;

/// <summary>
/// Chooses which kind of tokenizer a llama-family checkpoint carries and reads it, in the order the inference
/// engine's own converter chooses.
/// </summary>
/// <remarks>
/// <para>
/// THE ORDER IS THE RULE, and it is decided by which files are present rather than by anything a configuration
/// says. A <c>tokenizer.model</c> makes the checkpoint a SentencePiece one, whatever else sits beside it; with
/// no <c>tokenizer.model</c>, a <c>tokenizer.json</c> whose model is a byte-fallback BPE is the second road;
/// and with neither, the byte-level GPT-2 file set - <c>vocab.json</c> and <c>merges.txt</c> - is the third.
/// This version reads the first and the third and refuses the second by name.
/// </para>
/// <para>
/// One key is written BEFORE the road is chosen, so both exports carry it from the same place:
/// <c>tokenizer.ggml.add_space_prefix</c>, from <c>tokenizer_config.json</c>'s <c>add_prefix_space</c>, and only
/// when the file says it.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY NOT HERE. After the vocabulary is read, the engine's converter applies two rules keyed
/// off the vocabulary SIZE alone: a checkpoint of 32,016 tokens is given four fixed infilling token identifiers,
/// and one of 49,152 tokens is given a false <c>add_bos_token</c>. Neither is reproduced, for one reason: both
/// write keys whose POSITION among the others decides the bytes of the file, and the only checkpoints that
/// could show that position are model families too large to check in as a fixture. A checkpoint of either size
/// therefore converts to a file that is correct and loadable but is not byte-identical to the engine's, and
/// that is recorded here, in THIRD-PARTY-NOTICES and in the maintainer notes rather than guessed at.
/// </para>
/// </remarks>
internal static class VocabularyExport
{
    /// <summary>Reads whichever tokenizer a checkpoint carries.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="config">The checkpoint's configuration.</param>
    /// <param name="tokenizerConfig">The parsed <c>tokenizer_config.json</c>, or <see langword="null"/>.</param>
    /// <param name="addedSpecialTokens">
    /// Token contents the caller says are added and special, or <see langword="null"/> for none. They are the
    /// byte-level route's input; a SentencePiece checkpoint declares its own in files that are always read.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The vocabulary to write.</returns>
    /// <exception cref="NotSupportedException">The checkpoint's tokenizer is one this version does not read.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="addedSpecialTokens"/> was supplied for a checkpoint that declares its own.
    /// </exception>
    internal static async Task<TokenizerExport> LoadAsync(string directory, HuggingFaceConfig config,
        TokenizerConfig tokenizerConfig, IReadOnlyList<string> addedSpecialTokens,
        CancellationToken cancellationToken)
    {
        bool isSentencePiece = File.Exists(Path.Combine(directory, SentencePieceModel.FileName));
        RequireReadableTokenizerFiles(directory, isSentencePiece, addedSpecialTokens);

        return isSentencePiece
            ? await SentencePieceTokenizerExport
                .LoadAsync(directory, config, tokenizerConfig, cancellationToken).ConfigureAwait(false)
            : await Gpt2BpeTokenizerExport
                .LoadAsync(directory, config, tokenizerConfig, addedSpecialTokens, cancellationToken)
                .ConfigureAwait(false);
    }

    private static void RequireReadableTokenizerFiles(string directory, bool isSentencePiece,
        IReadOnlyList<string> addedSpecialTokens)
    {
        if (File.Exists(Path.Combine(directory, "tekken.json"))
            && !File.Exists(Path.Combine(directory, "tokenizer.json")))
        {
            throw new NotSupportedException("The checkpoint ships a tekken.json, so its tokenizer is one this " +
                "version does not read. This version reads a SentencePiece tokenizer.model and the GPT-2 " +
                "byte-level file set.");
        }

        if (isSentencePiece)
        {
            if (addedSpecialTokens != null && addedSpecialTokens.Count > 0)
            {
                throw new ArgumentException("ConvertOptions.AddedSpecialTokens was supplied for a checkpoint " +
                    "whose tokenizer is a SentencePiece model. Such a checkpoint declares what its added " +
                    "tokens are, in added_tokens.json and in tokenizer_config.json, and both are read - so " +
                    "there is nothing here for a caller to supply.", "options");
            }

            return;
        }

        if (File.Exists(Path.Combine(directory, "tokenizer.json")))
        {
            throw new NotSupportedException("The checkpoint ships a tokenizer.json and no tokenizer.model. " +
                "Reading that file needs the publisher's own tokenizer library, which a conversion never " +
                "runs. This version reads a SentencePiece tokenizer.model and the GPT-2 byte-level file set - " +
                "vocab.json and merges.txt.");
        }
    }
}
