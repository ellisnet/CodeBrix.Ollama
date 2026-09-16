using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The completion path against a real model: SmolLM 360M, small enough to download and run in a test but a
/// real GGUF with a real tokenizer, which the conformance model deliberately is not.
/// </summary>
/// <remarks>
/// Gated behind <see cref="TestGates.LiveTests"/>, because the class downloads about 370 MB on its first run
/// and then really generates. Every test shares one loaded model through
/// <see cref="SmolLmModelFixture"/>.
/// </remarks>
public sealed class SmolLmLiveTests : IClassFixture<SmolLmModelFixture>
{
    private readonly SmolLmModelFixture fixture;
    private readonly ITestOutputHelper output;

    /// <summary>Creates the test class with its shared model and its output.</summary>
    /// <param name="fixture">The shared model.</param>
    /// <param name="output">Where the observed rates are written.</param>
    public SmolLmLiveTests(SmolLmModelFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    /// <summary>A probe reads the architecture and the embedded chat template without loading the weights.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task ProbeAsync_reads_the_architecture_and_the_chat_template()
    {
        //Arrange
        string path = await fixture.PathAsync(TestContext.Current.CancellationToken);

        //Act
        ModelDetails details = await ModelRunner.ProbeAsync(path, TestContext.Current.CancellationToken);

        //Assert
        details.Architecture.Should().Be("llama");
        details.ChatTemplate.Should().NotBeNull();
        details.ChatTemplate.Should().Contain("<|im_start|>");
        details.EmbeddingLength.Should().Be(SmolLmModelFixture.EmbeddingLength);
        details.TrainingContextLength.Should().BeGreaterThan(0);
        details.WeightsSize.Should().BeGreaterThan(0UL);
        details.ParameterCount.Should().BeGreaterThan(0UL);
        details.FileSize.Should().Be(SmolLmModelFixture.Size);
        details.Metadata.Should().ContainKey("general.architecture");
    }

    /// <summary>A model with a chat template resolves to the Jinja dialect on its own.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task LoadAsync_resolves_the_jinja_dialect_from_the_embedded_template()
    {
        //Arrange and act
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);

        //Assert
        model.ChatTemplateDialect.Should().Be(ChatTemplateDialect.Jinja);
        model.Details.ChatTemplate.Should().NotBeNull();
    }

    /// <summary>Text survives a trip through the tokenizer and back.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task TokenizeAsync_and_DetokenizeAsync_round_trip()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);

        //Act
        IReadOnlyList<int> tokens = await model.TokenizeAsync(
            "Hello, world!", false, true, TestContext.Current.CancellationToken);
        string text = await model.DetokenizeAsync(tokens, false, TestContext.Current.CancellationToken);

        //Assert
        tokens.Should().NotBeEmpty();
        text.Should().Be("Hello, world!");
    }

    /// <summary>A greedy completion of a fact the model knows produces that fact.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateToEndAsync_completes_a_prompt_the_model_knows()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 16,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            "The capital of France is", options, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"completion: '{result.Text}'");
        output.WriteLine($"rate: {result.Statistics.TokensPerSecond:F2} tokens/s");
        result.Text.Should().Contain("Paris");
        result.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
        result.Statistics.PromptTokens.Should().BeGreaterThan(0);
    }

    /// <summary>A streaming completion arrives in pieces and ends with a final update carrying the statistics.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateAsync_streams_updates_and_ends_with_the_statistics()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 24,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        List<GenerationUpdate> updates = new List<GenerationUpdate>();
        await foreach (GenerationUpdate update in model.GenerateAsync(
            "Count from one to ten:", options, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        //Assert
        updates.Should().HaveCountGreaterThanOrEqualTo(2);

        GenerationUpdate last = updates[updates.Count - 1];
        last.IsFinal.Should().BeTrue();
        last.Statistics.Should().NotBeNull();
        last.FinishReason.Should().Be(FinishReason.Length);
        last.Statistics.GeneratedTokens.Should().Be(24);
        output.WriteLine($"rate: {last.Statistics.TokensPerSecond:F2} tokens/s");

        for (int i = 0; i < updates.Count - 1; i++)
        {
            updates[i].IsFinal.Should().BeFalse();
            updates[i].FinishReason.Should().Be(FinishReason.None);
            updates[i].Tokens.Should().NotBeEmpty();
        }
    }

    /// <summary>A stop sequence ends the generation and is itself kept out of the text.</summary>
    /// <remarks>
    /// The stop sequence is taken from what this model really produces for this prompt rather than guessed
    /// at, so the test checks the detector rather than the model's taste: a greedy completion is
    /// reproducible, so the second run produces the same text and has to be cut at the same place.
    /// </remarks>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateToEndAsync_honours_a_stop_sequence()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        const string prompt = "Count: 1, 2, 3, 4, 5";

        GenerationOptions unconstrained = new GenerationOptions
        {
            MaxTokens = 24,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        GenerationResult baseline = await model.GenerateToEndAsync(
            prompt, unconstrained, TestContext.Current.CancellationToken);

        baseline.Text.Length.Should().BeGreaterThan(8);
        string stop = baseline.Text.Substring(4, 3);
        int cutAt = baseline.Text.IndexOf(stop, StringComparison.Ordinal);

        GenerationOptions constrained = new GenerationOptions
        {
            MaxTokens = 24,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        constrained.StopSequences.Add(stop);

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            prompt, constrained, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"stop sequence '{stop}' cut '{baseline.Text}' to '{result.Text}'");
        result.FinishReason.Should().Be(FinishReason.StopSequence);
        result.Text.Should().Be(baseline.Text.Substring(0, cutAt));
        result.Text.Should().NotContain(stop);
    }

    /// <summary>A grammar constrains the output to what the grammar allows and nothing else.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateToEndAsync_honours_a_grammar()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 8,
            Grammar = "root ::= \"yes\" | \"no\"",
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            "Is the sky blue? Answer yes or no.", options, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"constrained: '{result.Text}'");
        result.Text.Trim().Should().BeOneOf("yes", "no");
    }

    /// <summary>A grammar the engine cannot parse is a grammar problem, said so.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateToEndAsync_refuses_a_grammar_that_does_not_parse()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions { MaxTokens = 4, Grammar = "this is not gbnf (" };

        //Act and assert
        await Assert.ThrowsAsync<GrammarException>(() => model.GenerateToEndAsync(
            "anything", options, TestContext.Current.CancellationToken));
    }

    /// <summary>Repeating a prompt re-uses what is already in the context rather than evaluating it again.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateToEndAsync_reuses_the_cached_prompt_prefix()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);

        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 4,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        const string prompt = "The quick brown fox jumps over the lazy dog, and then";

        //Act
        GenerationResult first = await model.GenerateToEndAsync(
            prompt, options, TestContext.Current.CancellationToken);
        GenerationResult second = await model.GenerateToEndAsync(
            prompt, options, TestContext.Current.CancellationToken);

        //Assert
        first.Statistics.CachedPromptTokens.Should().Be(0);
        second.Statistics.CachedPromptTokens.Should().BeGreaterThan(0);
        (second.Statistics.CachedPromptTokens < second.Statistics.PromptTokens).Should().BeTrue();
        output.WriteLine(
            $"cached {second.Statistics.CachedPromptTokens} of {second.Statistics.PromptTokens} prompt tokens");
    }

    /// <summary>Clearing the cache makes the next request evaluate its whole prompt again.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task ClearCacheAsync_makes_the_next_request_evaluate_its_whole_prompt()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 2,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        const string prompt = "A sentence that is long enough to be worth caching at all.";
        await model.GenerateToEndAsync(prompt, options, TestContext.Current.CancellationToken);

        //Act
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);
        GenerationResult result = await model.GenerateToEndAsync(
            prompt, options, TestContext.Current.CancellationToken);

        //Assert
        result.Statistics.CachedPromptTokens.Should().Be(0);
    }

    /// <summary>A generative model embeds, through a second context created for it on demand.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task EmbedAsync_returns_one_vector_per_input()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);

        //Act
        EmbeddingResult embeddings = await model.EmbedAsync(
            new[] { "the first input", "a quite different second input" },
            TestContext.Current.CancellationToken);

        //Assert
        embeddings.Dimensions.Should().Be(SmolLmModelFixture.EmbeddingLength);
        embeddings.Embeddings.Should().HaveCount(2);
        embeddings.Embeddings[0].Should().HaveCount(SmolLmModelFixture.EmbeddingLength);
        embeddings.Embeddings[1].Should().HaveCount(SmolLmModelFixture.EmbeddingLength);
        embeddings.PromptTokens.Should().BeGreaterThan(0);

        bool anyNonZero = false;
        foreach (float value in embeddings.Embeddings[0])
        {
            float.IsFinite(value).Should().BeTrue();
            if (Math.Abs(value) > 0f) anyNonZero = true;
        }

        anyNonZero.Should().BeTrue();
    }

    /// <summary>Embedding leaves the generation context's cache alone, because it uses its own context.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task EmbedAsync_leaves_the_generation_cache_alone()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 2,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        const string prompt = "Embedding must not disturb this prompt's place in the cache at all.";
        await model.GenerateToEndAsync(prompt, options, TestContext.Current.CancellationToken);

        //Act
        await model.EmbedAsync(new[] { "unrelated" }, TestContext.Current.CancellationToken);
        GenerationResult result = await model.GenerateToEndAsync(
            prompt, options, TestContext.Current.CancellationToken);

        //Assert
        result.Statistics.CachedPromptTokens.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The seam the chat wave sits on: a prompt that has already been rendered and tokenized elsewhere goes
    /// straight into the same decode loop, and the engine supplies the beginning-of-sequence token the
    /// vocabulary asks for when the caller says the prompt has none.
    /// </summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateFromTokensAsync_decodes_a_prompt_that_was_tokenized_elsewhere()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<int> tokens = await model.TokenizeAsync(
            "The capital of France is", false, true, TestContext.Current.CancellationToken);

        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 8,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        RunningModel engine = (RunningModel)model;
        StringBuilder text = new StringBuilder();
        GenerationStatistics statistics = null;

        await foreach (GenerationUpdate update in engine.GenerateFromTokensAsync(
            tokens, options, false, TestContext.Current.CancellationToken))
        {
            text.Append(update.Text);
            if (update.IsFinal) statistics = update.Statistics;
        }

        //Assert
        output.WriteLine($"from tokens: '{text}'");
        text.ToString().Should().Contain("Paris");
        statistics.Should().NotBeNull();
        statistics.GeneratedTokens.Should().Be(8);
    }

    /// <summary>Cancelling a stream stops it promptly rather than at the end of the request.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task GenerateAsync_stops_promptly_when_it_is_cancelled()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 4096,
            Sampling = new SamplingOptions { Temperature = 0.8f, Seed = 7 },
        };

        using CancellationTokenSource source = new CancellationTokenSource();
        Stopwatch clock = new Stopwatch();

        //Act
        OperationCanceledException caught = null;

        try
        {
            await foreach (GenerationUpdate update in model.GenerateAsync(
                "Write a very long story about a lighthouse.", options, source.Token))
            {
                if (update.IsFinal) break;

                clock.Restart();
                source.Cancel();
            }
        }
        catch (OperationCanceledException error)
        {
            // The clock is stopped here and nowhere later: everything after this point - an assertion
            // helper, a string format - would otherwise be counted as time the engine took to stop.
            clock.Stop();
            caught = error;
        }

        //Assert
        caught.Should().NotBeNull();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        output.WriteLine($"cancelled after {clock.Elapsed.TotalMilliseconds:F0} ms");

        // The model is still usable afterwards: cancelling ends a request, it does not break the context.
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);
        GenerationResult after = await model.GenerateToEndAsync(
            "Two plus two is",
            new GenerationOptions { MaxTokens = 4, Sampling = new SamplingOptions { Temperature = 0f } },
            TestContext.Current.CancellationToken);

        after.Text.Should().NotBeNull();
    }
}
