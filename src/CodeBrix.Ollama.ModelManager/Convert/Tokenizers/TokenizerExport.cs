using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221, gguf-py/gguf/vocab.py@b10221;

/// <summary>
/// A tokenizer as it is written into a GGUF file: which tokenizer model it is, the pre-tokenizer the engine
/// should use, the vocabulary in identifier order with a type for each entry, the merge table, the identifiers
/// of the special tokens, the flags that say whether the engine adds them, and the chat template.
/// </summary>
internal sealed class TokenizerExport
{
    /// <summary>The tokenizer model name, for example <c>gpt2</c>.</summary>
    internal string Model { get; set; }

    /// <summary>The pre-tokenizer the engine recognises, for example <c>gpt-2</c>.</summary>
    internal string Pre { get; set; }

    /// <summary>The vocabulary in identifier order, with gaps filled by placeholders.</summary>
    internal List<string> Tokens { get; } = new List<string>();

    /// <summary>
    /// The score of each entry of <see cref="Tokens"/>, when the tokenizer has scores. A byte-level BPE has
    /// none and leaves this empty, so the key is not written at all.
    /// </summary>
    internal List<float> Scores { get; } = new List<float>();

    /// <summary>What each entry of <see cref="Tokens"/> is.</summary>
    internal List<int> TokenTypes { get; } = new List<int>();

    /// <summary>The merge table, one <c>left right</c> pair per entry, in rank order.</summary>
    internal List<string> Merges { get; } = new List<string>();

    /// <summary>The special token identifiers, keyed by kind, in the order they were found.</summary>
    internal OrderedDictionary<string, long> SpecialTokenIds { get; } =
        new OrderedDictionary<string, long>(StringComparer.Ordinal);

    /// <summary>Whether the engine should add a special token of each kind, in the order they were found.</summary>
    internal OrderedDictionary<string, bool> AddSpecialTokens { get; } =
        new OrderedDictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>The chat template, when the tokenizer configuration carries one.</summary>
    internal string ChatTemplate { get; set; }

    /// <summary>Whether the tokenizer configuration says a space is put in front of the text.</summary>
    internal bool? AddSpacePrefix { get; set; }

    /// <summary>Writes the tokenizer's key-values, in the order the engine's converter writes them.</summary>
    /// <param name="writer">The file being built.</param>
    internal void WriteTo(GgufWriter writer)
    {
        if (AddSpacePrefix.HasValue)
        {
            writer.AddBoolean("tokenizer.ggml.add_space_prefix", AddSpacePrefix.Value);
        }

        writer.AddString("tokenizer.ggml.model", Model);
        writer.AddString("tokenizer.ggml.pre", Pre);
        writer.AddArray("tokenizer.ggml.tokens", GgufValueType.String, Tokens.ToArray());
        writer.AddArray("tokenizer.ggml.scores", GgufValueType.Float32, Scores.ToArray());
        writer.AddArray("tokenizer.ggml.token_type", GgufValueType.Int32, TokenTypes.ToArray());
        writer.AddArray("tokenizer.ggml.merges", GgufValueType.String, Merges.ToArray());

        foreach (KeyValuePair<string, long> entry in SpecialTokenIds)
        {
            string key = GetSpecialTokenKey(entry.Key);
            if (key == null)
            {
                // The engine warns and moves on when it has no handler for a kind; so does this.
                continue;
            }

            writer.AddUInt32(key, (uint)entry.Value);
        }

        foreach (KeyValuePair<string, bool> entry in AddSpecialTokens)
        {
            string key = GetAddSpecialTokenKey(entry.Key);
            if (key == null)
            {
                continue;
            }

            writer.AddBoolean(key, entry.Value);
        }

        if (ChatTemplate != null)
        {
            writer.AddString("tokenizer.chat_template", ChatTemplate);
        }
    }

    private static string GetSpecialTokenKey(string kind)
    {
        switch (kind)
        {
            case "bos": return "tokenizer.ggml.bos_token_id";
            case "eos": return "tokenizer.ggml.eos_token_id";
            case "unk": return "tokenizer.ggml.unknown_token_id";
            case "sep": return "tokenizer.ggml.seperator_token_id";
            case "pad": return "tokenizer.ggml.padding_token_id";
            case "mask": return "tokenizer.ggml.mask_token_id";
            case "eot": return "tokenizer.ggml.eot_token_id";
            case "eom": return "tokenizer.ggml.eom_token_id";
            default: return null;
        }
    }

    private static string GetAddSpecialTokenKey(string kind)
    {
        switch (kind)
        {
            case "bos": return "tokenizer.ggml.add_bos_token";
            case "eos": return "tokenizer.ggml.add_eos_token";
            case "sep": return "tokenizer.ggml.add_sep_token";
            default: return null;
        }
    }
}
