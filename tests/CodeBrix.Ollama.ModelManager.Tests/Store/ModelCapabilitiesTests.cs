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
    public async Task Infer_WithoutAPoolingType_ReportsCompletion()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel());

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Equal(new List<ModelCapability> { ModelCapability.Completion });
    }

    [Fact]
    public async Task Infer_WithAPoolingType_ReportsEmbeddingInsteadOfCompletion()
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
    public async Task Infer_WithAToolChatTemplate_ReportsTools()
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
    public async Task Infer_WithAThinkTagChatTemplate_ReportsThinking()
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
    public async Task Infer_WithAPlainChatTemplate_ReportsNeitherToolsNorThinking()
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
    public async Task Infer_WithVisionBlocks_ReportsVision()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel().AddUInt32("llama.vision.block_count", 4));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Vision);
    }

    [Fact]
    public async Task Infer_WithAudioBlocks_ReportsAudio()
    {
        //Arrange
        GgufMetadata metadata = await ReadAsync(BaseModel().AddUInt32("llama.audio.block_count", 4));

        //Act
        IReadOnlyList<ModelCapability> capabilities = ModelCapabilities.Infer(null, metadata, null, null);

        //Assert
        capabilities.Should().Contain(ModelCapability.Audio);
    }

    [Fact]
    public async Task Infer_WithAProjector_ReportsVisionOnly()
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
    public async Task Infer_WithAnAudioProjector_ReportsAudioAsWell()
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
    public async Task Infer_WithAFalseAudioEncoderFlag_DoesNotReportAudio()
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
    public async Task Infer_WithAGoTemplateUsingTools_ReportsTools()
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
    public async Task Infer_WithAGoTemplateUsingSuffix_ReportsInsert()
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
    public async Task Infer_WithAGoTemplateMentioningThinking_ReportsThinking()
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
    public void Infer_WithConfigCapabilities_ReportsThemEvenWithoutAnyGguf()
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
    public async Task Infer_WithTheSameCapabilityFromTwoSources_ReportsItOnce()
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
