using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/llama.py@b10221, conversion/base.py@b10221;

/// <summary>
/// The Llama family as the converter sees it: which hyper-parameters become which GGUF keys and in what order,
/// which tensors take the rotary permutation, and which tensors stay at full precision whatever output type was
/// asked for.
/// </summary>
/// <remarks>
/// <para>
/// This is a port of the inference engine's own Llama converter, not a re-derivation. The key order matters:
/// a GGUF file's key-values are written in the order they were added, and a file whose keys are in a different
/// order is a different file even when it means the same thing.
/// </para>
/// <para>
/// The rotary permutation is the one piece that cannot be guessed. The engine lays the query and key
/// projections out interleaved by head rather than split in halves, so the rows are shuffled on the way in -
/// and the same shuffle is applied to the query and key biases when a checkpoint has them.
/// </para>
/// </remarks>
internal sealed class LlamaArchitecture
{
    /// <summary>The architecture name written as <c>general.architecture</c>.</summary>
    internal const string ArchitectureName = "llama";

    private static readonly string[] ContextLengthKeys =
    {
        "max_position_embeddings", "n_ctx", "n_positions", "max_length", "max_sequence_length",
        "model_max_length",
    };

    private static readonly string[] EmbeddingLengthKeys = { "hidden_size", "n_embd", "dim" };

    private static readonly string[] FeedForwardLengthKeys =
    {
        "prefix_dense_intermediate_size", "dense_intermediate_size", "intermediate_size", "n_inner", "hidden_dim",
    };

    private static readonly string[] HeadCountKeys = { "num_attention_heads", "n_head", "n_heads" };

    private static readonly string[] HeadCountKvKeys = { "num_key_value_heads", "n_kv_heads" };

    private static readonly string[] BlockCountKeys =
    {
        "n_layers", "num_hidden_layers", "n_layer", "num_layers",
    };

    private static readonly string[] RopeThetaKeys =
    {
        "global_rope_theta", "rope_global_theta", "rope_theta_global", "rope_theta", "rotary_emb_base",
    };

    private static readonly string[] RmsEpsilonKeys = { "rms_norm_eps", "norm_eps" };

    private static readonly string[] LayerNormEpsilonKeys =
    {
        "layer_norm_eps", "layer_norm_epsilon", "norm_epsilon",
    };

    private static readonly string[] SkippedSuffixes =
    {
        ".attention.masked_bias", ".attention.bias", ".rotary_emb.inv_freq",
    };

    /// <summary>The <c>architectures</c> entries this version reads as the Llama family.</summary>
    internal static readonly string[] SupportedModelArchitectures =
    {
        "LlamaForCausalLM", "LLaMAForCausalLM", "MistralForCausalLM", "LlamaModel",
    };

    private readonly HuggingFaceConfig _config;
    private readonly LlamaTensorNameMap _names;
    private readonly bool _prefixTensorNames;

    internal LlamaArchitecture(HuggingFaceConfig config, string modelArchitecture)
    {
        _config = config;
        if (!config.TryGetInt64(BlockCountKeys, out long blockCount))
        {
            throw new CheckpointFormatException(
                "The checkpoint's config.json does not say how many layers the model has.");
        }

        BlockCount = (int)blockCount;
        _names = new LlamaTensorNameMap(BlockCount);
        _prefixTensorNames = string.Equals(modelArchitecture, "LlamaModel", StringComparison.Ordinal);

        if (!config.TryGetInt64(HeadCountKeys, out long headCount))
        {
            throw new CheckpointFormatException(
                "The checkpoint's config.json does not say how many attention heads the model has.");
        }

        HeadCount = headCount;
        HeadCountKv = config.TryGetInt64(HeadCountKvKeys, out long kv) ? kv : headCount;
        RequireNoRopeScaling(config);
    }

    /// <summary>How many transformer blocks the model has.</summary>
    internal int BlockCount { get; }

    /// <summary>How many attention heads the model has.</summary>
    internal long HeadCount { get; }

    /// <summary>How many key and value heads the model has; the same as the head count unless it is grouped.</summary>
    internal long HeadCountKv { get; }

    /// <summary>Whether a checkpoint tensor is one the converter drops rather than writes.</summary>
    /// <param name="name">The checkpoint's name for the tensor.</param>
    /// <returns><see langword="true"/> when the tensor is dropped.</returns>
    internal static bool IsDropped(string name)
    {
        for (int i = 0; i < SkippedSuffixes.Length; i++)
        {
            if (name.EndsWith(SkippedSuffixes[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Maps a checkpoint tensor name on to its GGUF name.</summary>
    /// <param name="name">The checkpoint's name for the tensor.</param>
    /// <returns>The GGUF name.</returns>
    internal string MapTensorName(string name)
    {
        string source = _prefixTensorNames ? "model." + name : name;
        if (!_names.TryMap(source, out string mapped))
        {
            throw new NotSupportedException("The checkpoint holds a tensor called \"" + name +
                "\", which is not part of the llama architecture this converter knows.");
        }

        return mapped;
    }

    /// <summary>
    /// How many heads the rotary permutation of a tensor is grouped by, or zero when the tensor is not permuted.
    /// </summary>
    /// <param name="name">The checkpoint's name for the tensor.</param>
    /// <returns>The head count to permute by, or 0.</returns>
    internal long GetPermuteHeadCount(string name)
    {
        if (name.EndsWith("q_proj.weight", StringComparison.Ordinal)
            || name.EndsWith("q_proj.bias", StringComparison.Ordinal))
        {
            return HeadCount;
        }

        if (name.EndsWith("k_proj.weight", StringComparison.Ordinal)
            || name.EndsWith("k_proj.bias", StringComparison.Ordinal))
        {
            return HeadCount != HeadCountKv ? HeadCountKv : HeadCount;
        }

        return 0;
    }

    /// <summary>Decides the type one tensor is written as.</summary>
    /// <param name="mappedName">The GGUF name of the tensor.</param>
    /// <param name="dimensions">How many dimensions it has.</param>
    /// <param name="fileType">The file type the conversion settled on.</param>
    /// <returns>The ggml type to write.</returns>
    internal GgufTensorType GetTensorType(string mappedName, int dimensions, GgufFileType fileType)
    {
        if (dimensions <= 1 || mappedName.EndsWith("_norm.weight", StringComparison.Ordinal))
        {
            return GgufTensorType.F32;
        }

        if (IsExpertGate(mappedName))
        {
            return GgufTensorType.F32;
        }

        if (!EndsWithAny(mappedName, ".weight", ".lora_a", ".lora_b"))
        {
            return GgufTensorType.F32;
        }

        switch (fileType)
        {
            case GgufFileType.F32:
                return GgufTensorType.F32;
            case GgufFileType.F16:
                return GgufTensorType.F16;
            case GgufFileType.BF16:
                return GgufTensorType.BF16;
            default:
                throw new NotSupportedException("The file type " + fileType + " is not one this converter writes.");
        }
    }

    /// <summary>Writes the architecture's key-values, in the order the engine's converter writes them.</summary>
    /// <param name="writer">The file being built.</param>
    /// <param name="fileType">The file type the conversion settled on.</param>
    internal void WriteParameters(GgufWriter writer, GgufFileType fileType)
    {
        writer.AddUInt32("llama.block_count", (uint)BlockCount);
        if (_config.TryGetInt64(ContextLengthKeys, out long contextLength))
        {
            writer.AddUInt32("llama.context_length", (uint)contextLength);
        }

        if (_config.TryGetInt64(EmbeddingLengthKeys, out long embeddingLength))
        {
            writer.AddUInt32("llama.embedding_length", (uint)embeddingLength);
        }

        if (_config.TryGetInt64(FeedForwardLengthKeys, out long feedForwardLength))
        {
            writer.AddUInt32("llama.feed_forward_length", (uint)feedForwardLength);
        }

        if (_config.TryGetInt64(HeadCountKeys, out long headCount))
        {
            writer.AddUInt32("llama.attention.head_count", (uint)headCount);
        }

        if (_config.TryGetInt64(HeadCountKvKeys, out long headCountKv))
        {
            writer.AddUInt32("llama.attention.head_count_kv", (uint)headCountKv);
        }

        if (_config.TryGetBoolean("is_causal", out bool isCausal) && !isCausal)
        {
            writer.AddBoolean("llama.attention.causal", false);
        }

        if (TryGetRopeTheta(out double ropeTheta))
        {
            writer.AddFloat32("llama.rope.freq_base", (float)ropeTheta);
        }

        if (_config.TryGetDouble(RmsEpsilonKeys, out double rmsEpsilon))
        {
            writer.AddFloat32("llama.attention.layer_norm_rms_epsilon", (float)rmsEpsilon);
        }

        if (_config.TryGetDouble(LayerNormEpsilonKeys, out double layerNormEpsilon))
        {
            writer.AddFloat32("llama.attention.layer_norm_epsilon", (float)layerNormEpsilon);
        }

        if (_config.TryGetInt64(new[] { "head_dim" }, out long headDimension))
        {
            writer.AddUInt32("llama.attention.key_length", (uint)headDimension);
            writer.AddUInt32("llama.attention.value_length", (uint)headDimension);
        }

        writer.AddUInt32("general.file_type", (uint)fileType);

        if (_config.TryGetInt64(new[] { "vocab_size" }, out long vocabSize))
        {
            writer.AddUInt32("llama.vocab_size", (uint)vocabSize);
        }

        writer.AddUInt32("llama.rope.dimension_count", (uint)GetRopeDimensionCount());
    }

    private long GetRopeDimensionCount()
    {
        if (_config.TryGetInt64(new[] { "head_dim" }, out long headDimension))
        {
            return headDimension;
        }

        if (_config.TryGetInt64(EmbeddingLengthKeys, out long embeddingLength) && HeadCount > 0)
        {
            return embeddingLength / HeadCount;
        }

        throw new CheckpointFormatException(
            "The checkpoint's config.json does not say how wide an attention head is.");
    }

    private bool TryGetRopeTheta(out double value)
    {
        // The engine mirrors the model's own rope_theta into its rope parameters when the parameters do not
        // carry one of their own, and reads it back from there.
        if (_config.TryGetNestedDouble("rope_parameters", "rope_theta", out value)
            || _config.TryGetNestedDouble("rope_scaling", "rope_theta", out value))
        {
            return true;
        }

        return _config.TryGetDouble(RopeThetaKeys, out value);
    }

    private static void RequireNoRopeScaling(HuggingFaceConfig config)
    {
        string ropeType = config.GetNestedString("rope_parameters", "rope_type")
            ?? config.GetNestedString("rope_scaling", "rope_type")
            ?? config.GetNestedString("rope_parameters", "type")
            ?? config.GetNestedString("rope_scaling", "type");
        if (ropeType == null)
        {
            return;
        }

        throw new NotSupportedException("The checkpoint's config.json asks for the rope scaling \"" + ropeType +
            "\". This version converts models whose rotary embedding is unscaled.");
    }

    private static bool IsExpertGate(string mappedName)
    {
        // The only tensor of the engine's "always float32" list that the llama architecture owns.
        return mappedName.StartsWith("blk.", StringComparison.Ordinal)
            && mappedName.EndsWith(".ffn_gate_inp.weight", StringComparison.Ordinal);
    }

    private static bool EndsWithAny(string value, params string[] suffixes)
    {
        for (int i = 0; i < suffixes.Length; i++)
        {
            if (value.EndsWith(suffixes[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
