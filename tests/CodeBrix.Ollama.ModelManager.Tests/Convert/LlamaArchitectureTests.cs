using System;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the Llama architecture mapping: the tensor-name table, which tensors take the rotary permutation,
/// which stay at full precision, and the two transformers defaults a conversion has to fill in for itself.
/// </summary>
public sealed class LlamaArchitectureTests
{
    [Theory]
    [InlineData("model.embed_tokens.weight", "token_embd.weight")]
    [InlineData("lm_head.weight", "output.weight")]
    [InlineData("model.norm.weight", "output_norm.weight")]
    [InlineData("model.layers.1.self_attn.q_proj.weight", "blk.1.attn_q.weight")]
    [InlineData("model.layers.1.self_attn.k_proj.bias", "blk.1.attn_k.bias")]
    [InlineData("model.layers.0.self_attn.v_proj.weight", "blk.0.attn_v.weight")]
    [InlineData("model.layers.0.self_attn.o_proj.weight", "blk.0.attn_output.weight")]
    [InlineData("model.layers.0.mlp.gate_proj.weight", "blk.0.ffn_gate.weight")]
    [InlineData("model.layers.0.mlp.up_proj.weight", "blk.0.ffn_up.weight")]
    [InlineData("model.layers.0.mlp.down_proj.weight", "blk.0.ffn_down.weight")]
    [InlineData("model.layers.0.input_layernorm.weight", "blk.0.attn_norm.weight")]
    [InlineData("model.layers.0.post_attention_layernorm.weight", "blk.0.ffn_norm.weight")]
    [InlineData("layers.1.attention.wq.weight", "blk.1.attn_q.weight")]
    public void TryMap_maps_a_publisher_name_on_to_the_engine_name(string source, string expected)
    {
        //Arrange
        var map = new LlamaTensorNameMap(2);

        //Act
        bool mapped = map.TryMap(source, out string result);

        //Assert
        mapped.Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryMap_answers_nothing_for_a_tensor_of_another_architecture()
    {
        //Arrange
        var map = new LlamaTensorNameMap(2);

        //Act
        bool mapped = map.TryMap("model.layers.0.mixer.in_proj.weight", out string _);

        //Assert
        mapped.Should().BeFalse();
    }

    [Fact]
    public async Task GetPermuteHeadCount_permutes_the_query_and_key_projections_and_nothing_else()
    {
        //Arrange
        LlamaArchitecture architecture = await LoadAsync("tinyllamagqa-115k");

        //Act and assert
        architecture.GetPermuteHeadCount("model.layers.0.self_attn.q_proj.weight").Should().Be(4L);
        architecture.GetPermuteHeadCount("model.layers.0.self_attn.q_proj.bias").Should().Be(4L);
        architecture.GetPermuteHeadCount("model.layers.0.self_attn.k_proj.weight").Should().Be(2L);
        architecture.GetPermuteHeadCount("model.layers.0.self_attn.k_proj.bias").Should().Be(2L);
        architecture.GetPermuteHeadCount("model.layers.0.self_attn.v_proj.weight").Should().Be(0L);
        architecture.GetPermuteHeadCount("model.embed_tokens.weight").Should().Be(0L);
    }

    [Theory]
    [InlineData("blk.0.attn_q.weight", 2, GgufTensorType.BF16)]
    [InlineData("blk.0.attn_norm.weight", 1, GgufTensorType.F32)]
    [InlineData("blk.0.attn_q.bias", 1, GgufTensorType.F32)]
    [InlineData("blk.0.ffn_gate_inp.weight", 2, GgufTensorType.F32)]
    [InlineData("token_embd.weight", 2, GgufTensorType.BF16)]
    public async Task GetTensorType_keeps_the_tensors_the_engine_keeps_at_full_precision(string name,
        int dimensions, GgufTensorType expected)
    {
        //Arrange
        LlamaArchitecture architecture = await LoadAsync("tinyllama-123k");

        //Act
        GgufTensorType type = architecture.GetTensorType(name, dimensions, GgufFileType.BF16);

        //Assert
        type.Should().Be(expected);
    }

    [Theory]
    [InlineData(".attention.masked_bias")]
    [InlineData(".attention.bias")]
    [InlineData(".rotary_emb.inv_freq")]
    public void IsDropped_drops_the_buffers_the_engine_drops(string suffix)
        => LlamaArchitecture.IsDropped("model.layers.0" + suffix).Should().BeTrue();

    [Fact]
    public async Task HuggingFaceConfig_fills_in_the_head_width_the_model_class_would_default()
    {
        //Arrange
        using var checkpoint = new TempCheckpointDirectory("tinyllama-123k");

        //Act
        HuggingFaceConfig config = await HuggingFaceConfig.LoadAsync(checkpoint.DirectoryPath,
            TestContext.Current.CancellationToken);

        //Assert
        config.Contains("head_dim").Should().BeTrue();
        config.TryGetInt64(new[] { "head_dim" }, out long headDimension).Should().BeTrue();
        headDimension.Should().Be(16L);
    }

    [Fact]
    public void HuggingFaceConfig_falls_back_to_the_attention_head_count_for_the_key_value_heads()
    {
        //Arrange
        string json = "{\"model_type\":\"llama\",\"hidden_size\":64,\"num_attention_heads\":4,"
            + "\"num_hidden_layers\":2}";

        //Act
        HuggingFaceConfig config = HuggingFaceConfig.Parse(Encoding.UTF8.GetBytes(json), "config.json");

        //Assert
        config.TryGetInt64(new[] { "num_key_value_heads" }, out long heads).Should().BeTrue();
        heads.Should().Be(4L);
        config.TryGetInt64(new[] { "head_dim" }, out long headDimension).Should().BeTrue();
        headDimension.Should().Be(16L);
    }

    [Fact]
    public void HuggingFaceConfig_leaves_a_configuration_that_is_not_llama_alone()
    {
        //Arrange
        string json = "{\"model_type\":\"gpt2\",\"hidden_size\":64,\"num_attention_heads\":4}";

        //Act
        HuggingFaceConfig config = HuggingFaceConfig.Parse(Encoding.UTF8.GetBytes(json), "config.json");

        //Assert
        config.Contains("head_dim").Should().BeFalse();
        config.Contains("num_key_value_heads").Should().BeFalse();
    }

    [Fact]
    public void HuggingFaceConfig_refuses_a_file_that_is_not_json()
    {
        //Act
        Action act = () => HuggingFaceConfig.Parse(Encoding.UTF8.GetBytes("not json"), "config.json");

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message.Should().Contain("not valid JSON");
    }

    private static async Task<LlamaArchitecture> LoadAsync(string variant)
    {
        HuggingFaceConfig config = await HuggingFaceConfig.LoadAsync(
            ConvertFixtureFiles.CheckpointPath(variant), TestContext.Current.CancellationToken);
        return new LlamaArchitecture(config, "LlamaForCausalLM");
    }
}
