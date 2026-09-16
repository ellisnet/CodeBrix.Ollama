using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the capability inference: what the config declares, what the model's own GGUF metadata says,
/// what the projectors bring, and what the Go template asks for.
/// </summary>
public sealed class ModelCapabilitiesTests
{
    [Fact]
    public async Task Infer_without_a_pooling_type_reports_completion()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Equal(new List<ModelCapability> { ModelCapability.Completion });
    }

    [Fact]
    public async Task Infer_with_a_pooling_type_reports_embedding_instead_of_completion()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel().AddUInt32("llama.pooling_type", 1));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Embedding);
        capabilities.Should().NotContain(ModelCapability.Completion);
    }

    [Fact]
    public async Task Infer_with_a_tool_chat_template_reports_tools()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(
            BaseModel().AddString("tokenizer.chat_template", "{% if tools %}{{ tools }}{% endif %}"));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Tools);
    }

    [Fact]
    public async Task Infer_with_a_think_tag_chat_template_reports_thinking()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(
            BaseModel().AddString("tokenizer.chat_template", "<think>{{ reasoning }}</think>"));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Thinking);
    }

    [Fact]
    public async Task Infer_with_a_plain_chat_template_reports_neither_tools_nor_thinking()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(
            BaseModel().AddString("tokenizer.chat_template", "{{ messages }}"));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().NotContain(ModelCapability.Tools);
        capabilities.Should().NotContain(ModelCapability.Thinking);
    }

    [Fact]
    public async Task Infer_with_vision_blocks_reports_vision()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel().AddUInt32("llama.vision.block_count", 4));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Vision);
    }

    [Fact]
    public async Task Infer_with_audio_blocks_reports_audio()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel().AddUInt32("llama.audio.block_count", 4));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Audio);
    }

    [Fact]
    public async Task Infer_with_a_projector_reports_vision_only()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());
        GgufMetadata projector = await ReadAsync(new GgufTestFileBuilder()
            .AddString("general.architecture", "clip")
            .AddString("general.type", "projector")
            .AddBool("clip.has_vision_encoder", true));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(
            null, metadata, new List<GgufMetadata> { projector }, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Vision);
        capabilities.Should().NotContain(ModelCapability.Audio);
    }

    [Fact]
    public async Task Infer_with_an_audio_projector_reports_audio_as_well()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());
        GgufMetadata projector = await ReadAsync(new GgufTestFileBuilder()
            .AddString("general.architecture", "clip")
            .AddString("general.type", "projector")
            .AddBool("clip.has_audio_encoder", true));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(
            null, metadata, new List<GgufMetadata> { projector }, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Vision);
        capabilities.Should().Contain(ModelCapability.Audio);
    }

    [Fact]
    public async Task Infer_with_a_false_audio_encoder_flag_does_not_report_audio()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());
        GgufMetadata projector = await ReadAsync(new GgufTestFileBuilder()
            .AddString("general.architecture", "clip")
            .AddString("general.type", "projector")
            .AddBool("clip.has_audio_encoder", false));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(
            null, metadata, new List<GgufMetadata> { projector }, null);

        //Assert
        capabilities.Should().NotContain(ModelCapability.Audio);
    }

    [Fact]
    public async Task Infer_with_a_go_template_using_tools_reports_tools()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(
            null, metadata, null, "{{ if .Tools }}{{ .Tools }}{{ end }}");

        //Assert
        capabilities.Should().Contain(ModelCapability.Tools);
    }

    [Fact]
    public async Task Infer_with_a_go_template_using_suffix_reports_insert()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(
            null, metadata, null, "{{ .Prompt }}{{ .Suffix }}");

        //Assert
        capabilities.Should().Contain(ModelCapability.Insert);
    }

    [Fact]
    public async Task Infer_with_a_go_template_mentioning_thinking_reports_thinking()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(
            null, metadata, null, "{{ if .Thinking }}<think>{{ .Thinking }}</think>{{ end }}");

        //Assert
        capabilities.Should().Contain(ModelCapability.Thinking);
    }

    [Fact]
    public void Infer_with_config_capabilities_reports_them_even_without_any_gguf()
    {
        //Arrange
        var config = new ModelConfig { Capabilities = new List<string> { "completion", "tools", "nonsense" } };

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(config, null, null, null);

        //Assert
        capabilities.Should().Equal(new List<ModelCapability>
        {
            ModelCapability.Completion,
            ModelCapability.Tools
        });
    }

    [Fact]
    public async Task Infer_with_the_same_capability_from_two_sources_reports_it_once()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(
            BaseModel().AddString("tokenizer.chat_template", "{% if tools %}{{ tools }}{% endif %}"));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(
            null, metadata, null, "{{ .Tools }}");

        //Assert
        capabilities.Should().Equal(new List<ModelCapability>
        {
            ModelCapability.Tools,
            ModelCapability.Completion
        });
    }

    /// <summary>
    /// A plain text model with nothing special declared.
    /// </summary>
    /// <returns>The builder, so that further keys can be added.</returns>
    private static GgufTestFileBuilder BaseModel()
    {
        return new GgufTestFileBuilder()
            .AddString("general.architecture", "llama")
            .AddUInt32("llama.block_count", 2);
    }

    /// <summary>
    /// Builds a GGUF file in memory and reads its metadata back.
    /// </summary>
    /// <param name="builder">The file to build.</param>
    /// <returns>The metadata.</returns>
    private static async Task<GgufMetadata> ReadAsync(GgufTestFileBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        return await GgufMetadata.ReadAsync(stream, null, TestContext.Current.CancellationToken);
    }
}
