using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager; //was previously: gguf-py/gguf/tensor_mapping.py@b10221;

/// <summary>
/// The table that turns a publisher's tensor name into the name the inference engine expects, for the Llama
/// family: <c>model.layers.3.self_attn.q_proj.weight</c> becomes <c>blk.3.attn_q.weight</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the Llama slice of the engine's own table, ported rather than re-derived: the alternatives under each
/// target are the spellings different publishers use for the same tensor, and dropping one would silently
/// refuse a checkpoint that the engine's converter accepts. Every entry the engine lists for this architecture
/// is here, in its order.
/// </para>
/// <para>
/// A name is looked up whole first, and then with <c>.weight</c> or <c>.bias</c> taken off and put back on the
/// target, which is how a bias finds the same target its weight does.
/// </para>
/// </remarks>
internal sealed class LlamaTensorNameMap
{
    private static readonly string[] Suffixes = { ".weight", ".bias" };

    private static readonly (string Target, string[] Sources)[] GeneralMappings =
    {
        ("token_embd", new[]
        {
            "gpt_neox.embed_in",
            "transformer.wte",
            "transformer.word_embeddings",
            "word_embeddings",
            "model.embed_tokens",
            "embed_tokens",
            "tok_embeddings",
            "embeddings.word_embeddings",
            "embeddings.tok_embeddings",
            "wte",
            "transformer.embd.wte",
            "model.tok_embeddings",
            "model.embedding",
            "backbone.embedding",
            "backbone.embeddings",
            "transformer.in_out_embed",
            "embedding.word_embeddings",
            "transformer.token_embeddings",
            "shared",
            "rwkv.embeddings",
            "model.embeddings",
            "model.word_embeddings",
            "encoder",
            "model.transformer.wte",
            "model.embed",
        }),
        ("output", new[]
        {
            "embed_out",
            "lm_head",
            "output",
            "word_embeddings_for_head",
            "lm_head.linear",
            "output_layer",
            "head",
            "head.out",
            "model.transformer.ff_out",
            "head.decoder",
        }),
        ("output_norm", new[]
        {
            "gpt_neox.final_layer_norm",
            "transformer.ln_f",
            "model.norm",
            "norm",
            "transformer.norm_f",
            "ln_f",
            "model.final_layernorm",
            "lm_head.ln",
            "model.norm_f",
            "backbone.norm_f",
            "transformer.rms_norm",
            "encoder.final_layernorm",
            "transformer.norm",
            "rwkv.ln_out",
            "model.ln_out",
            "backbone.final_layer_norm",
            "model.transformer.ln_f",
            "final_norm",
        }),
        ("rope_freqs", new[]
        {
            "rope.freqs",
            "rotary_pos_emb.inv_freq",
        }),
    };

    private static readonly (string Target, string[] Sources)[] BlockMappings =
    {
        ("blk.{bid}.attn_norm", new[]
        {
            "gpt_neox.layers.{bid}.input_layernorm",
            "transformer.h.{bid}.ln_1",
            "transformer.blocks.{bid}.norm_1",
            "transformer.h.{bid}.input_layernorm",
            "h.{bid}.input_layernorm",
            "transformer.h.{bid}.ln_mlp",
            "model.layers.{bid}.input_layernorm",
            "layers.{bid}.attention_norm",
            "model.layers.{bid}.ln1",
            "h.{bid}.ln_1",
            "transformer.h.{bid}.ln",
            "model.layers.layers.{bid}.norm",
            "model.layers.layers.{bid}.pre_mixer_norm",
            "model.layers.{bid}.attention_norm",
            "model.layers.{bid}.norm",
            "backbone.layers.{bid}.norm",
            "transformer.decoder_layer.{bid}.rms_norm",
            "model.layers.{bid}.pre_attn_norm",
            "transformer.blocks.{bid}.norm_attn_norm.norm_1",
            "encoder.layers.{bid}.input_layernorm",
            "transformer.layers.{bid}.attn_norm",
            "rwkv.blocks.{bid}.ln1",
            "layers.{bid}.input_layernorm",
            "transformer_encoder.{bid}.attention_norm",
            "layers.{bid}.attn_norm",
            "model.layers.{bid}.operator_norm",
            "model.transformer.blocks.{bid}.attn_norm",
            "model.layers.{bid}.attention_layernorm",
            "model.layers.{bid}.pre_attention_layernorm",
        }),
        ("blk.{bid}.attn_q", new[]
        {
            "model.layers.{bid}.self_attn.q_proj",
            "layers.{bid}.self_attn.q_proj",
            "model.layers.{bid}.self_attn.q_proj_no_perm",
            "layers.{bid}.attention.wq",
            "encoder.layer.{bid}.attention.self.query",
            "transformer.layer.{bid}.attention.q_lin",
            "transformer.h.{bid}.attn.q_proj",
            "model.layers.layers.{bid}.self_attn.q_proj",
            "model.layers.{bid}.attention.wq",
            "transformer.decoder_layer.{bid}.multi_head_attention.query",
            "transformer.h.{bid}.attn.attention.q_proj",
            "model.transformer.blocks.{bid}.q_proj",
            "backbone.layers.{bid}.mixer.q_proj",
            "model.blocks.{bid}.attn.attn_query",
        }),
        ("blk.{bid}.attn_k", new[]
        {
            "model.layers.{bid}.self_attn.k_proj",
            "layers.{bid}.self_attn.k_proj",
            "model.layers.{bid}.self_attn.k_proj_no_perm",
            "layers.{bid}.attention.wk",
            "encoder.layer.{bid}.attention.self.key",
            "transformer.layer.{bid}.attention.k_lin",
            "transformer.h.{bid}.attn.k_proj",
            "transformer.h.{bid}.attn.k",
            "model.layers.layers.{bid}.self_attn.k_proj",
            "model.layers.{bid}.attention.wk",
            "transformer.decoder_layer.{bid}.multi_head_attention.key",
            "transformer.h.{bid}.attn.attention.k_proj",
            "model.transformer.blocks.{bid}.k_proj",
            "backbone.layers.{bid}.mixer.k_proj",
            "model.blocks.{bid}.attn.attn_key",
        }),
        ("blk.{bid}.attn_v", new[]
        {
            "model.layers.{bid}.self_attn.v_proj",
            "layers.{bid}.self_attn.v_proj",
            "layers.{bid}.attention.wv",
            "encoder.layer.{bid}.attention.self.value",
            "transformer.layer.{bid}.attention.v_lin",
            "transformer.h.{bid}.attn.v_proj",
            "transformer.h.{bid}.attn.v",
            "model.layers.layers.{bid}.self_attn.v_proj",
            "model.layers.{bid}.attention.wv",
            "transformer.decoder_layer.{bid}.multi_head_attention.value",
            "transformer.h.{bid}.attn.attention.v_proj",
            "model.transformer.blocks.{bid}.v_proj",
            "backbone.layers.{bid}.mixer.v_proj",
            "model.blocks.{bid}.attn.attn_value",
        }),
        ("blk.{bid}.attn_output", new[]
        {
            "gpt_neox.layers.{bid}.attention.dense",
            "transformer.h.{bid}.attn.c_proj",
            "transformer.blocks.{bid}.attn.out_proj",
            "transformer.h.{bid}.self_attention.dense",
            "h.{bid}.self_attention.dense",
            "model.layers.{bid}.self_attn.o_proj",
            "layers.{bid}.self_attn.o_proj",
            "model.layers.{bid}.self_attn.out_proj",
            "model.layers.{bid}.self_attn.linear_attn",
            "layers.{bid}.attention.wo",
            "encoder.layer.{bid}.attention.output.dense",
            "layers.{bid}.attn.Wo",
            "transformer.layer.{bid}.attention.out_lin",
            "transformer.h.{bid}.attn.out_proj",
            "model.layers.{bid}.self_attn.dense",
            "model.layers.{bid}.attention.dense",
            "h.{bid}.attn.c_proj",
            "transformer.h.{bid}.mixer.out_proj",
            "model.layers.layers.{bid}.self_attn.o_proj",
            "model.layers.layers.{bid}.mixer.o_proj",
            "model.layers.{bid}.attention.wo",
            "encoder.layers.{bid}.attn.out_proj",
            "encoder.layers.{bid}.mixer.out_proj",
            "transformer.decoder_layer.{bid}.multi_head_attention.linear",
            "transformer.blocks.{bid}.norm_attn_norm.attn.out_proj",
            "encoder.layers.{bid}.self_attention.dense",
            "transformer.layers.{bid}.attn.out_proj",
            "transformer.h.{bid}.attn.attention.out_proj",
            "transformer_encoder.{bid}.wo",
            "model.transformer.blocks.{bid}.attn_out",
            "backbone.layers.{bid}.mixer.o_proj",
            "model.layers.{bid}.self_attn.language_expert_dense",
            "model.blocks.{bid}.attn.attn_resid",
        }),
        ("blk.{bid}.attn_rot_embd", new[]
        {
            "model.layers.{bid}.self_attn.rotary_emb.inv_freq",
            "layers.{bid}.attention.inner_attention.rope.freqs",
            "model.layers.layers.{bid}.self_attn.rotary_emb.inv_freq",
            "transformer.h.{bid}.attn.rotary_emb.inv_freq",
        }),
        ("blk.{bid}.ffn_norm", new[]
        {
            "gpt_neox.layers.{bid}.post_attention_layernorm",
            "transformer.h.{bid}.ln_2",
            "h.{bid}.post_attention_layernorm",
            "transformer.blocks.{bid}.norm_2",
            "model.layers.{bid}.post_attention_layernorm",
            "layers.{bid}.ffn_norm",
            "model.layers.{bid}.ln2",
            "h.{bid}.ln_2",
            "model.layers.{bid}.ffn_norm",
            "transformer.decoder_layer.{bid}.rms_norm_2",
            "model.layers.{bid}.pre_moe_norm",
            "encoder.layers.{bid}.post_attention_layernorm",
            "transformer.layers.{bid}.ffn_norm",
            "model.layers.{bid}.pre_ff_layernorm",
            "model.layers.{bid}.pre_moe_layernorm",
            "transformer_encoder.{bid}.ffn_norm",
            "model.layers.layers.{bid}.pre_mlp_norm",
            "model.transformer.blocks.{bid}.ff_norm",
            "layers.{bid}.post_attention_layernorm",
            "model.layers.{bid}.feedforward_layernorm",
            "model.layers.{bid}.pre_mlp_layernorm",
            "layers.{bid}.mlp_norm",
        }),
        ("blk.{bid}.ffn_gate_inp", new[]
        {
            "layers.{bid}.feed_forward.gate",
            "model.layers.{bid}.block_sparse_moe.gate",
            "model.layers.{bid}.mlp.gate",
            "transformer.decoder_layer.{bid}.router",
            "transformer.blocks.{bid}.ffn.router.layer",
            "model.layers.{bid}.block_sparse_moe.router.layer",
            "model.layers.{bid}.feed_forward.router",
            "encoder.layers.{bid}.mlp.router.layer",
            "model.layers.{bid}.mlp.router",
            "model.layers.{bid}.mlp.gate.wg",
            "model.layers.{bid}.block_sparse_moe.primary_router",
            "model.layers.{bid}.feed_forward.gate",
            "model.layers.{bid}.mlp.router.gate",
            "layers.{bid}.gate",
            "backbone.layers.{bid}.mixer.gate",
            "model.layers.{bid}.moe.gate",
            "model.layers.{bid}.router.proj",
        }),
        ("blk.{bid}.ffn_up", new[]
        {
            "gpt_neox.layers.{bid}.mlp.dense_h_to_4h",
            "transformer.h.{bid}.mlp.c_fc",
            "transformer.blocks.{bid}.ffn.up_proj",
            "transformer.h.{bid}.mlp.dense_h_to_4h",
            "h.{bid}.mlp.dense_h_to_4h",
            "model.layers.{bid}.mlp.up_proj",
            "layers.{bid}.mlp.up_proj",
            "layers.{bid}.feed_forward.w3",
            "encoder.layer.{bid}.intermediate.dense",
            "layers.{bid}.mlp.Wi",
            "transformer.layer.{bid}.ffn.lin1",
            "transformer.h.{bid}.mlp.fc_in",
            "transformer.h.{bid}.mlp.linear_3",
            "model.layers.{bid}.mlp.dense_h_to_4h",
            "transformer.h.{bid}.mlp.w1",
            "h.{bid}.mlp.c_fc",
            "transformer.h.{bid}.mlp.fc1",
            "model.layers.{bid}.mlp.fc1",
            "model.layers.{bid}.mlp.gate_up_proj",
            "model.layers.layers.{bid}.mlp.up_proj",
            "model.layers.layers.{bid}.mlp.gate_up_proj",
            "model.layers.{bid}.feed_forward.w3",
            "encoder.layers.{bid}.mlp.fc11",
            "encoder.layers.{bid}.mlp.fc1",
            "model.layers.{bid}.mlp.c_fc",
            "encoder.layer.{bid}.mlp.gated_layers_v",
            "encoder.layer.{bid}.mlp.gated_layers",
            "encoder.layer.{bid}.mlp.up_gated_layer",
            "model.layers.{bid}.residual_mlp.w3",
            "encoder.layers.{bid}.mlp.dense_h_to_4h",
            "transformer.h.{bid}.mlp.c_fc_1",
            "model.layers.{bid}.feed_forward.up_proj",
            "transformer_encoder.{bid}.ffn.w12",
            "model.layers.{bid}.block_sparse_moe.up",
            "model.transformer.blocks.{bid}.up_proj",
            "backbone.layers.{bid}.mixer.up_proj",
            "model.layers.{bid}.mlp.language_mlp.up_proj",
            "model.blocks.{bid}.mlp.mlp_linear",
        }),
        ("blk.{bid}.ffn_up_exps", new[]
        {
            "layers.{bid}.feed_forward.experts.w3",
            "transformer.decoder_layer.{bid}.moe.linear_v",
            "transformer.blocks.{bid}.ffn.experts.mlp.v1",
            "model.layers.{bid}.mlp.experts.up_proj",
            "model.layers.{bid}.block_sparse_moe.experts.w3",
            "model.layers.{bid}.feed_forward.experts.up_proj",
            "encoder.layers.{bid}.mlp.experts.mlp.w1",
            "model.layers.{bid}.block_sparse_moe.experts.up",
            "model.layers.{bid}.moe.up_proj",
        }),
        ("blk.{bid}.ffn_gate", new[]
        {
            "model.layers.{bid}.mlp.gate_proj",
            "layers.{bid}.mlp.gate_proj",
            "layers.{bid}.feed_forward.w1",
            "transformer.h.{bid}.mlp.w2",
            "transformer.h.{bid}.mlp.c_fc2",
            "model.layers.layers.{bid}.mlp.gate_proj",
            "model.layers.{bid}.feed_forward.w1",
            "encoder.layers.{bid}.mlp.fc12",
            "encoder.layer.{bid}.mlp.gated_layers_w",
            "transformer.h.{bid}.mlp.linear_1",
            "model.layers.{bid}.residual_mlp.w1",
            "transformer.h.{bid}.mlp.c_fc_0",
            "model.layers.{bid}.feed_forward.gate_proj",
            "model.transformer.blocks.{bid}.ff_proj",
            "model.layers.{bid}.mlp.language_mlp.gate_proj",
            "model.blocks.{bid}.mlp.mlp_gate",
        }),
        ("blk.{bid}.ffn_gate_exps", new[]
        {
            "layers.{bid}.feed_forward.experts.w1",
            "transformer.decoder_layer.{bid}.moe.linear",
            "transformer.blocks.{bid}.ffn.experts.mlp.w1",
            "model.layers.{bid}.mlp.experts.gate_proj",
            "model.layers.{bid}.block_sparse_moe.experts.w1",
            "model.layers.{bid}.feed_forward.experts.gate_proj",
            "model.layers.{bid}.block_sparse_moe.experts.gate",
            "model.layers.{bid}.moe.gate_proj",
        }),
        ("blk.{bid}.ffn_down", new[]
        {
            "gpt_neox.layers.{bid}.mlp.dense_4h_to_h",
            "transformer.h.{bid}.mlp.c_proj",
            "transformer.blocks.{bid}.ffn.down_proj",
            "transformer.h.{bid}.mlp.dense_4h_to_h",
            "h.{bid}.mlp.dense_4h_to_h",
            "model.layers.{bid}.mlp.down_proj",
            "layers.{bid}.mlp.down_proj",
            "layers.{bid}.feed_forward.w2",
            "encoder.layer.{bid}.output.dense",
            "layers.{bid}.mlp.Wo",
            "transformer.layer.{bid}.ffn.lin2",
            "transformer.h.{bid}.mlp.fc_out",
            "model.layers.{bid}.mlp.dense_4h_to_h",
            "h.{bid}.mlp.c_proj",
            "transformer.h.{bid}.mlp.fc2",
            "model.layers.{bid}.mlp.fc2",
            "model.layers.layers.{bid}.mlp.down_proj",
            "model.layers.{bid}.feed_forward.w2",
            "encoder.layers.{bid}.mlp.fc2",
            "model.layers.{bid}.mlp.c_proj",
            "encoder.layer.{bid}.mlp.wo",
            "transformer.layers.{bid}.ffn.proj_2",
            "model.layers.{bid}.residual_mlp.w2",
            "encoder.layer.{bid}.mlp.down_layer",
            "encoder.layers.{bid}.mlp.dense_4h_to_h",
            "model.layers.h.{bid}.mlp.c_proj",
            "model.layers.{bid}.feed_forward.down_proj",
            "transformer_encoder.{bid}.ffn.w3",
            "model.layers.{bid}.block_sparse_moe.down",
            "model.transformer.blocks.{bid}.ff_out",
            "backbone.layers.{bid}.mixer.down_proj",
            "model.layers.{bid}.mlp.language_mlp.down_proj",
            "model.blocks.{bid}.mlp.mlp_resid",
        }),
        ("blk.{bid}.ffn_down_exps", new[]
        {
            "layers.{bid}.feed_forward.experts.w2",
            "transformer.decoder_layer.{bid}.moe.linear_1",
            "transformer.blocks.{bid}.ffn.experts.mlp.w2",
            "model.layers.{bid}.mlp.experts.down_proj",
            "model.layers.{bid}.block_sparse_moe.output_linear",
            "model.layers.{bid}.block_sparse_moe.experts.w2",
            "model.layers.{bid}.feed_forward.experts.down_proj",
            "encoder.layers.{bid}.mlp.experts.mlp.w2",
            "model.layers.{bid}.block_sparse_moe.experts.down",
            "model.layers.{bid}.moe.down_proj",
            "model.layers.{bid}.experts.down_proj",
        }),
        ("rope_freqs", new[]
        {
            "encoder.layers.{bid}.self_attention.rotary_emb.inv_freq",
        }),
    };

    private readonly Dictionary<string, string> _mapping = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Builds the table for a model with a given number of blocks.</summary>
    /// <param name="blockCount">How many transformer blocks the model has.</param>
    internal LlamaTensorNameMap(int blockCount)
    {
        for (int i = 0; i < GeneralMappings.Length; i++)
        {
            (string target, string[] sources) = GeneralMappings[i];
            _mapping[target] = target;
            for (int source = 0; source < sources.Length; source++)
            {
                _mapping[sources[source]] = target;
            }
        }

        for (int block = 0; block < blockCount; block++)
        {
            string id = block.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < BlockMappings.Length; i++)
            {
                (string target, string[] sources) = BlockMappings[i];
                string blockTarget = target.Replace("{bid}", id);
                _mapping[blockTarget] = blockTarget;
                for (int source = 0; source < sources.Length; source++)
                {
                    _mapping[sources[source].Replace("{bid}", id)] = blockTarget;
                }
            }
        }
    }

    /// <summary>Maps one checkpoint tensor name on to its GGUF name.</summary>
    /// <param name="name">The name the checkpoint uses.</param>
    /// <param name="mapped">Receives the GGUF name.</param>
    /// <returns><see langword="true"/> when the name is one this architecture knows.</returns>
    internal bool TryMap(string name, out string mapped)
    {
        if (_mapping.TryGetValue(name, out mapped))
        {
            return true;
        }

        for (int i = 0; i < Suffixes.Length; i++)
        {
            string suffix = Suffixes[i];
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            if (_mapping.TryGetValue(name.Substring(0, name.Length - suffix.Length), out string target))
            {
                mapped = target + suffix;
                return true;
            }
        }

        mapped = null;
        return false;
    }
}
