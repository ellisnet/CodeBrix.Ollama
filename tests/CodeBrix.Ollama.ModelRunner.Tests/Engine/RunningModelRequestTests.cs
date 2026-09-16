using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// What a request does to the model around it: how two of them share one instance, what a disposal in the
/// middle of one does, and what happens to a caller that reaches back into the model from inside its own
/// enumeration.
/// </summary>
/// <remarks>
/// Everything here runs offline against the tiny conformance model, whose file carries no tokenizer at all,
/// so the decode loop is reached through the internal hook that takes token ids. The twelve ids are the ones
/// the native conformance gate uses, and the token the model produces from them is the argmax of the last
/// recorded row of reference logits: a request that ran on a context something else had disturbed would not
/// produce it.
/// </remarks>
public sealed class RunningModelRequestTests
{
    private static ModelRunnerOptions Options() =>
        new ModelRunnerOptions
        {
            ModelPath = TestVectors.ConformanceModelPath,
            GpuLayers = 0,
            ContextSize = 64,
            FlashAttention = FlashAttentionMode.Disabled,
        };

    private static ModelRunnerOptions LongRunOptions()
    {
        ModelRunnerOptions options = Options();
        options.ContextSize = 4096;
        return options;
    }

    private static GenerationOptions LongRun() =>
        new GenerationOptions
        {
            MaxTokens = 4000,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

    private static GenerationOptions Greedy() =>
        new GenerationOptions
        {
            MaxTokens = 1,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

    /// <summary>The token the conformance prompt must produce: the argmax of its last row of logits.</summary>
    private static int ExpectedFirstToken()
    {
        IReadOnlyList<EngineExpectedLogitRow> expected = EngineExpectedLogits.Read(TestVectors.ExpectedPath);
        return expected[expected.Count - 1].Argmax;
    }

    /// <summary>The decode loop taking token ids produces the token the reference logits point at.</summary>
    [Fact]
    public async Task GenerateFromTokensAsync_produces_the_token_the_reference_logits_point_at()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        //Act
        List<int> tokens = await DrainAsync((RunningModel)model, TestContext.Current.CancellationToken);

        //Assert
        tokens.Should().HaveCount(1);
        tokens[0].Should().Be(ExpectedFirstToken());
    }

    /// <summary>Two enumerations of one model take their turns, and neither disturbs the other's answer.</summary>
    [Fact]
    public async Task GenerateFromTokensAsync_serialises_two_concurrent_enumerations()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;

        //Act
        Task<List<int>> first = Task.Run(
            () => DrainAsync(engine, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        Task<List<int>> second = Task.Run(
            () => DrainAsync(engine, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        List<int>[] both = await Task.WhenAll(first, second);

        //Assert
        int expected = ExpectedFirstToken();
        both[0].Should().HaveCount(1);
        both[1].Should().HaveCount(1);
        both[0][0].Should().Be(expected);
        both[1][0].Should().Be(expected);

        // The logits the context holds afterwards are still the reference ones, which they would not be if
        // the two requests had been interleaved in the context's memory.
        float[][] logits = await engine.DecodeForLogitsAsync(
            EngineExpectedLogits.Prompt, TestContext.Current.CancellationToken);

        logits.Should().HaveCount(EngineExpectedLogits.Prompt.Count);
        Argmax(logits[logits.Length - 1]).Should().Be(expected);
    }

    /// <summary>Disposing under an open enumeration ends it rather than freeing the context beneath it.</summary>
    /// <remarks>
    /// The enumeration is left running rather than being stepped to a pause: a model with no tokenizer
    /// produces no text, so nothing is yielded until the whole run is over and the first step is therefore
    /// the decode loop itself. That is the case the fix is about - the engine is inside a native call when
    /// the model is freed - and the run is long enough for the disposal to land in the middle of it.
    /// </remarks>
    [Fact]
    public async Task Dispose_under_an_open_enumeration_ends_it_with_ObjectDisposedException()
    {
        //Arrange
        IRunningModel model = await ModelRunner.LoadAsync(
            LongRunOptions(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;

        IAsyncEnumerator<GenerationUpdate> updates = engine
            .GenerateFromTokensAsync(
                EngineExpectedLogits.Prompt, LongRun(), false, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        //Act
        ValueTask<bool> step = updates.MoveNextAsync();
        model.Dispose();

        //Assert
        Func<Task> next = async () => await step;
        await next.Should().ThrowAsync<ObjectDisposedException>();

        Func<Task> clear = () => model.ClearCacheAsync(TestContext.Current.CancellationToken);
        await clear.Should().ThrowAsync<ObjectDisposedException>();

        // A second disposal, and the enumeration's own, still have to be ordinary.
        model.Dispose();
        await updates.DisposeAsync();
    }

    /// <summary>Disposing asynchronously under an open enumeration is no different.</summary>
    [Fact]
    public async Task DisposeAsync_under_an_open_enumeration_ends_it_with_ObjectDisposedException()
    {
        //Arrange
        IRunningModel model = await ModelRunner.LoadAsync(
            LongRunOptions(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;

        IAsyncEnumerator<GenerationUpdate> updates = engine
            .GenerateFromTokensAsync(
                EngineExpectedLogits.Prompt, LongRun(), false, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        //Act
        ValueTask<bool> step = updates.MoveNextAsync();
        await model.DisposeAsync();

        //Assert
        Func<Task> next = async () => await step;
        await next.Should().ThrowAsync<ObjectDisposedException>();

        await updates.DisposeAsync();
    }

    /// <summary>A gated member called from inside an enumeration of the same model is refused, not queued.</summary>
    [Fact]
    public async Task ClearCacheAsync_refuses_to_run_inside_an_enumeration_of_the_same_model()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;
        InvalidOperationException caught = null;

        //Act
        await foreach (GenerationUpdate update in engine.GenerateFromTokensAsync(
            EngineExpectedLogits.Prompt, Greedy(), false, TestContext.Current.CancellationToken))
        {
            Func<Task> clear = () => model.ClearCacheAsync(TestContext.Current.CancellationToken);
            caught = (await clear.Should().ThrowAsync<InvalidOperationException>()).Which;
        }

        //Assert
        caught.Should().NotBeNull();
        caught.Message.Should().Contain("enumeration of this model is in progress");
        caught.Message.Should().Contain("finish or dispose the enumeration first");
    }

    /// <summary>Embedding from inside an enumeration of the same model is refused in the same way.</summary>
    [Fact]
    public async Task EmbedAsync_refuses_to_run_inside_an_enumeration_of_the_same_model()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;
        InvalidOperationException caught = null;

        //Act
        await foreach (GenerationUpdate update in engine.GenerateFromTokensAsync(
            EngineExpectedLogits.Prompt, Greedy(), false, TestContext.Current.CancellationToken))
        {
            Func<Task> embed = () => model.EmbedAsync(new[] { "anything" }, TestContext.Current.CancellationToken);
            caught = (await embed.Should().ThrowAsync<InvalidOperationException>()).Which;
        }

        //Assert
        caught.Should().NotBeNull();
        caught.Message.Should().Contain(nameof(IRunningModel.EmbedAsync));
    }

    /// <summary>A second enumeration started from inside the first is refused rather than deadlocking.</summary>
    [Fact]
    public async Task GenerateFromTokensAsync_refuses_a_second_enumeration_on_the_same_call_chain()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;
        InvalidOperationException caught = null;

        //Act
        await foreach (GenerationUpdate update in engine.GenerateFromTokensAsync(
            EngineExpectedLogits.Prompt, Greedy(), false, TestContext.Current.CancellationToken))
        {
            Action second = () => engine.GenerateFromTokensAsync(
                EngineExpectedLogits.Prompt, Greedy(), false, TestContext.Current.CancellationToken);
            caught = second.Should().Throw<InvalidOperationException>().Which;
        }

        //Assert
        caught.Should().NotBeNull();
    }

    /// <summary>A batch the engine rejects as invalid input says so in its own words.</summary>
    [Fact]
    public async Task DecodeForLogitsAsync_explains_a_batch_the_engine_calls_invalid()
    {
        //Arrange
        await using IRunningModel model = await ModelRunner.LoadAsync(
            Options(), TestContext.Current.CancellationToken);

        RunningModel engine = (RunningModel)model;

        //Act
        Func<Task> act = () => engine.DecodeForLogitsAsync(new[] { 4096 }, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InferenceException>()).Which.Message.Should().Contain("invalid input");
    }

    /// <summary>A probe of a path that is not a path faults its task rather than throwing from the call.</summary>
    [Fact]
    public async Task ProbeAsync_reports_an_empty_path_on_the_task()
    {
        //Arrange
        Task<ModelDetails> probe = ModelRunner.ProbeAsync("   ", TestContext.Current.CancellationToken);

        //Act and assert
        probe.Should().NotBeNull();
        Func<Task> awaiting = () => probe;
        await awaiting.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>An embedding is as wide as the model's output, falling back to its hidden width.</summary>
    [Theory]
    [InlineData(768, 2048, 768)]
    [InlineData(0, 2048, 2048)]
    [InlineData(-1, 960, 960)]
    public void EmbeddingWidth_prefers_the_models_output_width(int output, int hidden, int expected)
        => RunningModel.EmbeddingWidth(output, hidden).Should().Be(expected);

    /// <summary>One input is limited by the physical batch, not by the logical one or the context alone.</summary>
    [Theory]
    [InlineData(2048, 512, 512)]
    [InlineData(256, 512, 256)]
    [InlineData(0, 512, 512)]
    [InlineData(2048, 0, 1)]
    public void EmbeddingInputLimit_is_the_physical_batch(int room, int physical, int expected)
        => RunningModel.EmbeddingInputLimit(room, physical).Should().Be(expected);

    private static async Task<List<int>> DrainAsync(RunningModel engine, CancellationToken cancellationToken)
    {
        List<int> tokens = new List<int>();

        await foreach (GenerationUpdate update in engine.GenerateFromTokensAsync(
            EngineExpectedLogits.Prompt, Greedy(), false, cancellationToken))
        {
            tokens.AddRange(update.Tokens);
        }

        return tokens;
    }

    private static int Argmax(float[] row)
    {
        int best = 0;
        for (int i = 1; i < row.Length; i++)
        {
            if (row[i] > row[best]) best = i;
        }

        return best;
    }
}
