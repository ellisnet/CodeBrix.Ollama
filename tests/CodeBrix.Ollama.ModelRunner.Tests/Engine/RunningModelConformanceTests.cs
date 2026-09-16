using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Loads the tiny conformance model through the public entry point and holds the engine behind it to the
/// same reference logits the native gate holds every native build to.
/// </summary>
/// <remarks>
/// <para>
/// The conformance model has no tokenizer at all - its vocabulary type is "none" - so nothing here can go in
/// as text. That is the point: it isolates the load, the context, the batch and the decode from everything
/// to do with text, and it is the only model whose exact logits are recorded anywhere. The twelve token ids
/// go in through an internal hook, and every one of the twelve rows of sixty-four logits has to match.
/// </para>
/// <para>
/// The same file is also probed, so that a probe and a real load are shown to agree about a model rather
/// than merely being believed to.
/// </para>
/// </remarks>
public sealed class RunningModelConformanceTests
{
    private const double Tolerance = 0.001;

    private static ModelRunnerOptions Options() =>
        new ModelRunnerOptions
        {
            ModelPath = TestVectors.ConformanceModelPath,
            GpuLayers = 0,
            ContextSize = 64,
            FlashAttention = FlashAttentionMode.Disabled,
        };

    /// <summary>A loaded model reports the shape the generator wrote into the file.</summary>
    [Fact]
    public async Task LoadAsync_reports_the_models_shape()
    {
        //Arrange and act
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        //Assert
        model.Details.Architecture.Should().Be("llama");
        model.Details.VocabularySize.Should().Be(64);
        model.Details.EmbeddingLength.Should().Be(32);
        model.Details.LayerCount.Should().Be(2);
        model.Details.HeadCount.Should().Be(4);
        model.Details.TrainingContextLength.Should().Be(64);
        model.Details.HasDecoder.Should().BeTrue();
        model.Details.HasEncoder.Should().BeFalse();
        model.Details.IsRecurrent.Should().BeFalse();
        model.Details.Path.Should().Be(TestVectors.ConformanceModelPath);
        model.Details.FileSize.Should().BeGreaterThan(0L);
        model.Details.Description.Should().Contain("llama");
        model.Details.Metadata.Should().ContainKey("general.architecture");
        model.Options.ModelPath.Should().Be(TestVectors.ConformanceModelPath);
    }

    /// <summary>A file with no chat template resolves to the engine's own template matching.</summary>
    [Fact]
    public async Task ChatTemplateDialect_falls_back_to_the_native_dialect_without_a_template()
    {
        //Arrange and act
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        //Assert
        model.Details.ChatTemplate.Should().BeNull();
        model.ChatTemplateDialect.Should().Be(ChatTemplateDialect.Native);
    }

    /// <summary>The engine pads a context up to its own minimum rather than honouring a smaller request.</summary>
    [Fact]
    public async Task LoadAsync_settles_on_the_engines_minimum_context()
    {
        //Arrange and act
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        //Assert
        RunningModel engine = (RunningModel)model;
        engine.ContextLength.Should().Be(256);
        engine.BatchLength.Should().BeGreaterThan(0);
    }

    /// <summary>A probe reports the same numbers as a load, without reading a weight.</summary>
    [Fact]
    public async Task ProbeAsync_reports_the_same_numbers_as_a_load()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        //Act
        ModelDetails probed = await ModelRunner.ProbeAsync(
            TestVectors.ConformanceModelPath, TestContext.Current.CancellationToken);

        //Assert
        probed.Architecture.Should().Be(model.Details.Architecture);
        probed.VocabularySize.Should().Be(model.Details.VocabularySize);
        probed.EmbeddingLength.Should().Be(model.Details.EmbeddingLength);
        probed.LayerCount.Should().Be(model.Details.LayerCount);
        probed.HeadCount.Should().Be(model.Details.HeadCount);
        probed.TrainingContextLength.Should().Be(model.Details.TrainingContextLength);
        probed.ParameterCount.Should().Be(model.Details.ParameterCount);
        probed.WeightsSize.Should().Be(model.Details.WeightsSize);
        probed.FileSize.Should().Be(model.Details.FileSize);
        probed.Metadata.Should().HaveCount(model.Details.Metadata.Count);

        // The two agree field for field only because the probe read the whole file; a probe that had to
        // fall back to the vocabulary-only load would report zero for the two tensor numbers, and says so.
        ModelDetailsBuilder.ReadOnlyTheVocabulary(probed).Should().BeFalse();
        ModelDetailsBuilder.ReadOnlyTheVocabulary(model.Details).Should().BeFalse();
        probed.ParameterCount.Should().BeGreaterThan(0UL);
    }

    /// <summary>A probe of a file that is not there says so as a load problem.</summary>
    [Fact]
    public async Task ProbeAsync_refuses_a_file_that_is_not_there()
    {
        //Arrange
        string missing = Path.Combine(AppContext.BaseDirectory, "not-a-model.gguf");

        //Act
        Func<Task> act = () => ModelRunner.ProbeAsync(missing, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelLoadException>();
    }

    /// <summary>Every logit the loaded model produces matches the recorded reference.</summary>
    [Fact]
    public async Task The_decode_path_reproduces_the_reference_logits()
    {
        //Arrange
        IReadOnlyList<EngineExpectedLogitRow> expected = EngineExpectedLogits.Read(TestVectors.ExpectedPath);
        expected.Should().HaveCount(EngineExpectedLogits.Prompt.Count);

        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        //Act
        RunningModel engine = (RunningModel)model;
        float[][] logits = await engine.DecodeForLogitsAsync(
            EngineExpectedLogits.Prompt, TestContext.Current.CancellationToken);

        //Assert
        logits.Should().HaveCount(expected.Count);

        double worst = 0.0;
        for (int position = 0; position < expected.Count; position++)
        {
            EngineExpectedLogitRow row = expected[position];
            row.Position.Should().Be(position);
            row.Token.Should().Be(EngineExpectedLogits.Prompt[position]);
            logits[position].Should().HaveCount(row.Logits.Length);

            int argmax = 0;
            for (int i = 0; i < row.Logits.Length; i++)
            {
                float value = logits[position][i];

                // A NaN would compare false against everything and so would pass a difference check; it has
                // to fail in its own right.
                float.IsFinite(value).Should().BeTrue();

                double difference = Math.Abs((double)value - row.Logits[i]);
                if (difference > worst) worst = difference;

                if (value > logits[position][argmax]) argmax = i;
            }

            argmax.Should().Be(row.Argmax);
        }

        (worst <= Tolerance).Should().BeTrue();
    }

    /// <summary>Clearing the cache really empties the record of what is in the context's memory.</summary>
    [Fact]
    public async Task ClearCacheAsync_empties_the_record_of_the_contexts_memory()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;
        await engine.DecodeForLogitsAsync(EngineExpectedLogits.Prompt, TestContext.Current.CancellationToken);
        engine.CachedTokenCount.Should().Be(EngineExpectedLogits.Prompt.Count);

        //Act
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);

        //Assert
        engine.CachedTokenCount.Should().Be(0);
    }

    /// <summary>A model that has been disposed refuses further work rather than crashing in native code.</summary>
    [Fact]
    public async Task A_disposed_model_refuses_further_work()
    {
        //Arrange
        IRunningModel model = await ModelRunner.LoadAsync(Options(), TestContext.Current.CancellationToken);

        //Act
        model.Dispose();
        model.Dispose();

        //Assert
        Func<Task> clear = () => model.ClearCacheAsync(TestContext.Current.CancellationToken);
        await clear.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>A named dialect with no template behind it fails the load, and says which one.</summary>
    [Fact]
    public async Task LoadAsync_refuses_a_named_dialect_with_no_template()
    {
        //Arrange
        ModelRunnerOptions options = Options();
        options.ChatTemplateDialect = ChatTemplateDialect.Jinja;

        //Act
        Func<Task> act = () => ModelRunner.LoadAsync(options, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelLoadException>()).Which.Message.Should().Contain("Jinja");
    }

    /// <summary>The dialect resolution table, exercised without a model behind it.</summary>
    [Theory]
    [InlineData(ChatTemplateDialect.Auto, false, false, false, ChatTemplateDialect.Native)]
    [InlineData(ChatTemplateDialect.Auto, false, false, true, ChatTemplateDialect.Jinja)]
    [InlineData(ChatTemplateDialect.Auto, false, true, false, ChatTemplateDialect.Jinja)]
    [InlineData(ChatTemplateDialect.Auto, true, false, false, ChatTemplateDialect.Ollama)]
    [InlineData(ChatTemplateDialect.Auto, true, true, true, ChatTemplateDialect.Ollama)]
    [InlineData(ChatTemplateDialect.Native, false, false, false, ChatTemplateDialect.Native)]
    [InlineData(ChatTemplateDialect.Native, true, true, true, ChatTemplateDialect.Native)]
    [InlineData(ChatTemplateDialect.Jinja, false, true, false, ChatTemplateDialect.Jinja)]
    [InlineData(ChatTemplateDialect.Jinja, false, false, true, ChatTemplateDialect.Jinja)]
    [InlineData(ChatTemplateDialect.Ollama, true, false, false, ChatTemplateDialect.Ollama)]
    public void ChatTemplateStrategy_resolves_the_dialect(
        ChatTemplateDialect requested, bool ollama, bool jinja, bool embedded, ChatTemplateDialect expected)
    {
        //Arrange
        string ollamaTemplate = ollama ? "{{ .Prompt }}" : null;
        string jinjaTemplate = jinja ? "{{ messages[0].content }}" : null;
        string embeddedTemplate = embedded ? "{{ messages[0]['content'] }}" : null;

        //Act
        ChatTemplateDialect resolved = ChatTemplateStrategy.Resolve(
            requested, ollamaTemplate, jinjaTemplate, embeddedTemplate);

        //Assert
        resolved.Should().Be(expected);
    }

    /// <summary>A dialect with no template behind it is refused, whichever one it is.</summary>
    [Theory]
    [InlineData(ChatTemplateDialect.Jinja)]
    [InlineData(ChatTemplateDialect.Ollama)]
    public void ChatTemplateStrategy_refuses_a_dialect_with_no_template(ChatTemplateDialect requested)
    {
        //Arrange
        Action act = () => ChatTemplateStrategy.Resolve(requested, null, null, null);

        //Assert
        act.Should().Throw<ModelLoadException>();
    }
}
