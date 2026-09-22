using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The whole text-generation driver over the tiny bundle: the load, the prefill, the decode, the cache, the
/// stop conditions, the streaming and every refusal - with nothing installed and nothing downloaded.
/// </summary>
/// <remarks>
/// The tiny bundle STOPS BY ITSELF: the length of the sequence so far is added to the end-of-sequence token's
/// score and to nothing else, and the length is read out of the attention mask INSIDE the graph, exactly as a
/// real bundle reads it. So a driver that built the mask wrongly, or lost the cache, would never stop - which
/// is what makes most of these tests fences rather than assertions about arithmetic.
/// </remarks>
public sealed class OnnxCausalLmModelTests
{
    private const string Prompt = "the quick";

    /// <summary>A bundle directory loads and says what it is.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_reads_what_the_bundle_says()
    {
        //Arrange and act
        await using IOnnxCausalLmModel model = await Load();

        //Assert
        model.Metadata.Architecture.Should().Be("tinyllama");
        model.Metadata.ModelFileName.Should().Be("model.onnx");
        model.Metadata.TokenizerKind.Should().Be("GPT-2 byte-level BPE");
        model.Metadata.LayerCount.Should().Be(2);
        model.Metadata.HeadCount.Should().Be(4);
        model.Metadata.KeyValueHeadCount.Should().Be(2);
        model.Metadata.HeadSize.Should().Be(8);
        model.Metadata.HiddenSize.Should().Be(32);
        model.Metadata.ContextLength.Should().Be(64);
        model.Metadata.VocabularySize.Should().Be(320);
        model.Metadata.BeginningOfSequenceTokenId.Should().Be(2);
        model.Metadata.EndOfSequenceTokenIds.Should().Equal(3);
        model.Metadata.PaddingTokenId.Should().Be(0);
        model.Metadata.MergeCount.Should().BeGreaterThan(0);
    }

    /// <summary>A set of (name to path) pairs loads the same model a directory does.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadFromFilesAsync_loads_the_same_model()
    {
        //Arrange and act
        await using IOnnxCausalLmModel model = await OnnxCausalLmModel.LoadFromFilesAsync(
            CausalLmFixtures.TinyBundleFiles(),
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.LayerCount.Should().Be(2);
    }

    /// <summary>The shared contract's own description of a model is filled in from the bundle.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Details_describes_the_bundle()
    {
        //Arrange and act
        await using IOnnxCausalLmModel model = await Load();

        //Assert
        model.Details.Architecture.Should().Be("tinyllama");
        model.Details.LayerCount.Should().Be(2);
        model.Details.HeadCount.Should().Be(4);
        model.Details.VocabularySize.Should().Be(320);
        model.Details.TrainingContextLength.Should().Be(64);
        model.Details.EmbeddingLength.Should().Be(32);
        model.Details.HasDecoder.Should().BeTrue();
        model.Details.HasEncoder.Should().BeFalse();
        model.Details.Path.Should().EndWith("model.onnx");
        model.Details.FileSize.Should().BeGreaterThan(0);
        model.Details.Metadata.Should().ContainKey("onnx.tokenizer");
    }

    /// <summary>The load settings the shared contract has a place for are the ones really in use.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Options_carries_the_settings_the_shared_contract_has_a_place_for()
    {
        //Arrange and act
        await using IOnnxCausalLmModel model = await Load(new OnnxRunnerOptions { Threads = 2 });

        //Assert
        model.Options.ModelPath.Should().EndWith("model.onnx");
        model.Options.Threads.Should().Be(2);
        model.Options.ContextSize.Should().Be(64u);
        model.RunnerOptions.Threads.Should().Be(2);
    }

    /// <summary>
    /// A cap the caller sets reaches the graph, because this driver passes the options object through: with
    /// no thread count stated, a cap of one leaves the graph running on one thread.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_honours_a_thread_cap()
    {
        //Arrange and act
        await using IOnnxCausalLmModel model = await Load(new OnnxRunnerOptions { MaxThreads = 1 });

        //Assert
        model.RunnerOptions.Threads.Should().Be(1);
        model.RunnerOptions.MaxThreads.Should().Be(1);
        model.Options.Threads.Should().Be(1);
    }

    /// <summary>A thread count the caller stated wins over a cap, here as everywhere.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_lets_a_stated_thread_count_win_over_the_cap()
    {
        //Arrange and act
        await using IOnnxCausalLmModel model = await Load(
            new OnnxRunnerOptions { Threads = 3, MaxThreads = 1 });

        //Assert
        model.RunnerOptions.Threads.Should().Be(3);
    }

    /// <summary>Tokenizing and detokenizing go through the bundle's own tokenizer.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task TokenizeAsync_and_DetokenizeAsync_round_trip()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        //Act
        IReadOnlyList<int> tokens = await model.TokenizeAsync(
            "the quick brown fox", cancellationToken: TestContext.Current.CancellationToken);
        string back = await model.DetokenizeAsync(
            tokens, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        tokens.Should().NotBeEmpty();
        back.Should().Be("the quick brown fox");
    }

    /// <summary>A generation ends on its own, and everything it wrote decodes back.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateToEndAsync_stops_on_the_end_of_sequence_token()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            Prompt, Greedy(100), TestContext.Current.CancellationToken);

        //Assert
        result.FinishReason.Should().Be(FinishReason.Stop);
        result.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
        result.Statistics.GeneratedTokens.Should().BeLessThan(100);
        result.Statistics.PromptTokens.Should().BeGreaterThan(0);
    }

    /// <summary>The same prompt gives the same tokens, twice over, on one loaded model.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_is_the_same_twice()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        //Act
        IReadOnlyList<int> first = await Generate(model, Prompt, Greedy(100));
        IReadOnlyList<int> second = await Generate(model, Prompt, Greedy(100));

        //Assert
        first.Should().Equal(second);
        first.Should().NotBeEmpty();
    }

    /// <summary>
    /// THE CACHE'S FENCE. Generating one token at a time with the cache fed back gives the same tokens as
    /// running the whole sequence through the graph in one go and taking the most likely token at every
    /// position. Nothing short of the cache being carried forward correctly produces that.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_agrees_with_one_run_over_the_whole_sequence()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        IReadOnlyList<int> prompt = await model.TokenizeAsync(
            Prompt, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<int> generated = await Generate(model, Prompt, Greedy(100));

        List<int> whole = new List<int>(prompt);
        whole.AddRange(generated);

        //Act - what the model answers with when each prefix is read from nothing, cache and all.
        List<int> withoutACache = new List<int>();
        for (int length = prompt.Count; length < whole.Count; length++)
        {
            withoutACache.Add(await AnswerFromNothing(whole.Take(length).ToList()));
        }

        //Assert
        withoutACache.Should().Equal(generated);
        generated.Should().NotBeEmpty();
    }

    /// <summary>A token limit ends the generation and says so.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateToEndAsync_stops_at_the_token_limit()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            Prompt, Greedy(3), TestContext.Current.CancellationToken);

        //Assert
        result.FinishReason.Should().Be(FinishReason.Length);
        result.Statistics.GeneratedTokens.Should().Be(3);
    }

    /// <summary>A stop sequence ends the generation and is not part of what comes back.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateToEndAsync_stops_on_a_stop_sequence()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        GenerationResult whole = await model.GenerateToEndAsync(
            Prompt, Greedy(100), TestContext.Current.CancellationToken);
        whole.Text.Should().NotBeEmpty();

        //The stop sequence is taken OUT of what the model wrote, so it is bound to be reached.
        string stop = whole.Text.Substring(whole.Text.Length / 2, 1);
        GenerationOptions options = Greedy(100);
        options.StopSequences.Add(stop);

        //Act
        GenerationResult stopped = await model.GenerateToEndAsync(
            Prompt, options, TestContext.Current.CancellationToken);

        //Assert
        stopped.FinishReason.Should().Be(FinishReason.StopSequence);
        stopped.Text.Should().NotContain(stop);
        whole.Text.Should().StartWith(stopped.Text);
    }

    /// <summary>The context filling up ends the generation and says so.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateToEndAsync_stops_when_the_context_fills_up()
    {
        //Arrange - a bundle whose context is shorter than the model's own patience.
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Replace("context_length", "64", "12");
        await using IOnnxCausalLmModel model = await OnnxCausalLmModel.LoadFromDirectoryAsync(
            bundle.DirectoryPath, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            Prompt, Greedy(100), TestContext.Current.CancellationToken);

        //Assert
        result.FinishReason.Should().Be(FinishReason.ContextFull);
    }

    /// <summary>A prompt longer than the context is refused before anything is run.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_with_a_prompt_longer_than_the_context_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Replace("context_length", "64", "4");
        await using IOnnxCausalLmModel model = await OnnxCausalLmModel.LoadFromDirectoryAsync(
            bundle.DirectoryPath, cancellationToken: TestContext.Current.CancellationToken);

        Func<Task> act = async () => await model.GenerateToEndAsync(
            "a much longer prompt than four tokens", Greedy(4), TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<InferenceException>())
            .Which.Message.Should().Contain("context holds");
    }

    /// <summary>An empty prompt starts from the token the bundle says a sequence begins with.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateToEndAsync_with_an_empty_prompt_starts_from_the_beginning_token()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            "", Greedy(100), TestContext.Current.CancellationToken);

        //Assert
        result.Statistics.PromptTokens.Should().Be(1);
        result.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
    }

    /// <summary>Text comes out as it is made, before the generation has finished.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_hands_over_the_first_update_before_it_has_finished()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        IAsyncEnumerator<GenerationUpdate> updates = model
            .GenerateAsync(Prompt, Greedy(100), TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        //Act
        bool moved = await updates.MoveNextAsync();

        //Assert
        moved.Should().BeTrue();
        updates.Current.IsFinal.Should().BeFalse();
        updates.Current.Tokens.Should().NotBeEmpty();

        await updates.DisposeAsync();
    }

    /// <summary>Walking away from a generation leaves the model usable.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_left_half_way_through_leaves_the_model_usable()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        IAsyncEnumerator<GenerationUpdate> updates = model
            .GenerateAsync(Prompt, Greedy(100), TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        //Act
        await updates.MoveNextAsync();
        await updates.DisposeAsync();

        //Assert
        (await Generate(model, Prompt, Greedy(100))).Should().NotBeEmpty();
    }

    /// <summary>Cancelling stops the loop between steps, and the model is usable afterwards.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_stops_when_it_is_cancelled()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        using CancellationTokenSource cancellation = new CancellationTokenSource();

        Func<Task> act = async () =>
        {
            int seen = 0;
            await foreach (GenerationUpdate update in model.GenerateAsync(
                Prompt, Greedy(100), cancellation.Token))
            {
                seen++;
                if (seen == 2) await cancellation.CancelAsync();
            }
        };

        //Act and assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await Generate(model, Prompt, Greedy(100))).Should().NotBeEmpty();
    }

    /// <summary>A second generation while one is in flight is refused rather than queued.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_while_one_is_in_flight_refuses()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        await using IAsyncEnumerator<GenerationUpdate> updates = model
            .GenerateAsync(Prompt, Greedy(100), TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await updates.MoveNextAsync();

        Func<Task> act = async () => await model.GenerateToEndAsync(
            Prompt, Greedy(100), TestContext.Current.CancellationToken);

        //Act and assert
        await act.Should().ThrowAsync<InferenceException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Func<Task> cancelledAttempt = async () => await model.GenerateToEndAsync(Prompt, Greedy(5), cancelled.Token);
        await cancelledAttempt.Should().ThrowAsync<OperationCanceledException>();
        await act.Should().ThrowAsync<InferenceException>();
        await updates.DisposeAsync();
        (await Generate(model, Prompt, Greedy(5))).Should().HaveCount(5);
    }

    /// <summary>Clearing the cache is allowed and does nothing, because nothing is held between requests.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ClearCacheAsync_completes()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        //Act
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);

        //Assert
        (await Generate(model, Prompt, Greedy(5))).Should().HaveCount(5);
    }

    /// <summary>Every chat member is refused by name.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_chat_members_are_refused_by_name()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        ChatRequest request = new ChatRequest();

        Action stream = () => model.ChatAsync(request, TestContext.Current.CancellationToken);
        Func<Task> whole = async () => await model.ChatToEndAsync(
            request, TestContext.Current.CancellationToken);
        Func<Task> render = async () => await model.RenderChatPromptAsync(
            request, TestContext.Current.CancellationToken);

        //Act and assert
        stream.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("ChatAsync");
        (await whole.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("ChatToEndAsync");
        (await render.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("RenderChatPromptAsync");
    }

    /// <summary>Embeddings are refused by name, with the reason.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task EmbedAsync_is_refused_by_name()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        Func<Task> act = async () => await model.EmbedAsync(
            new[] { "text" }, TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("EmbedAsync");
    }

    /// <summary>Adapters are refused by name, with the reason.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SetLoraAdaptersAsync_is_refused_by_name()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        Func<Task> act = async () => await model.SetLoraAdaptersAsync(
            Array.Empty<LoraAdapterOptions>(), TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("SetLoraAdaptersAsync");
    }

    /// <summary>Constraining the output is refused by the name of the setting that asked for it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_with_a_grammar_is_refused_by_name()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        Action grammar = () => model.GenerateAsync(
            Prompt, new GenerationOptions { Grammar = "root ::= \"a\"" },
            TestContext.Current.CancellationToken);
        Action schema = () => model.GenerateAsync(
            Prompt, new GenerationOptions { JsonSchema = "{}" }, TestContext.Current.CancellationToken);
        Action json = () => model.GenerateAsync(
            Prompt, new GenerationOptions { JsonMode = true }, TestContext.Current.CancellationToken);

        //Act and assert
        grammar.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("Grammar");
        schema.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("JsonSchema");
        json.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("JsonMode");
    }

    /// <summary>A token limit below one is refused where the caller wrote it.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateAsync_with_a_token_limit_below_one_refuses()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();
        Action act = () => model.GenerateAsync(
            Prompt, new GenerationOptions { MaxTokens = 0 }, TestContext.Current.CancellationToken);

        //Act and assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>A disposed model refuses everything rather than running on freed weights.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_disposed_model_refuses()
    {
        //Arrange
        IOnnxCausalLmModel model = await Load();
        model.Dispose();

        Action generate = () => model.GenerateAsync(Prompt, null, TestContext.Current.CancellationToken);
        Func<Task> tokenize = async () => await model.TokenizeAsync(
            Prompt, cancellationToken: TestContext.Current.CancellationToken);

        //Act and assert
        generate.Should().Throw<ObjectDisposedException>();
        await tokenize.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>Disposing twice is allowed.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Dispose_is_allowed_twice()
    {
        //Arrange
        IOnnxCausalLmModel model = await Load();

        //Act
        model.Dispose();
        Action act = () => model.Dispose();

        //Assert
        act.Should().NotThrow();
    }

    /// <summary>A directory that is not set is refused where the caller wrote it.</summary>
    [Fact]
    public void LoadFromDirectoryAsync_with_no_directory_refuses()
    {
        //Arrange
        Action act = () => OnnxCausalLmModel.LoadFromDirectoryAsync(
            " ", cancellationToken: TestContext.Current.CancellationToken);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>No files at all is refused where the caller wrote it.</summary>
    [Fact]
    public void LoadFromFilesAsync_with_no_files_refuses()
    {
        //Arrange
        Action act = () => OnnxCausalLmModel.LoadFromFilesAsync(
            null, cancellationToken: TestContext.Current.CancellationToken);

        //Act and assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// A generation configuration whose cache shape disagrees with the graph is refused at load, naming the
    /// tensor, rather than left to produce nonsense.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_with_a_head_size_the_graph_does_not_share_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Replace("head_size", "8", "16");

        Func<Task> act = async () => await OnnxCausalLmModel.LoadFromDirectoryAsync(
            bundle.DirectoryPath, cancellationToken: TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ModelLoadException>())
            .Which.Message.Should().Contain("head size");
    }

    /// <summary>
    /// A generation configuration naming a cache tensor the graph does not declare is refused at load, naming
    /// it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_with_a_cache_name_the_graph_does_not_declare_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.WriteConfiguration(
            bundle.ReadConfiguration().Replace(
                "past_key_values.%d.key", "past.%d.key", StringComparison.Ordinal));

        Func<Task> act = async () => await OnnxCausalLmModel.LoadFromDirectoryAsync(
            bundle.DirectoryPath, cancellationToken: TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ModelLoadException>())
            .Which.Message.Should().Contain("past.0.key");
    }

    /// <summary>Sampling with a seed gives the same text twice; another seed gives other text.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task GenerateToEndAsync_with_a_seed_is_reproducible()
    {
        //Arrange
        await using IOnnxCausalLmModel model = await Load();

        //Act
        IReadOnlyList<int> first = await Generate(model, Prompt, Sampled(12345));
        IReadOnlyList<int> second = await Generate(model, Prompt, Sampled(12345));
        IReadOnlyList<int> other = await Generate(model, Prompt, Sampled(98765));

        //Assert
        first.Should().Equal(second);
        first.Should().NotEqual(other);
    }

    private static GenerationOptions Greedy(int maximum) => new GenerationOptions
    {
        MaxTokens = maximum,
        Sampling = new SamplingOptions { Temperature = 0f, RepeatPenalty = 1f, RepeatLastN = 0 },
    };

    private static GenerationOptions Sampled(uint seed) => new GenerationOptions
    {
        MaxTokens = 12,
        Sampling = new SamplingOptions
        {
            Temperature = 1.2f, TopK = 20, TopP = 0.95f, RepeatPenalty = 1f, RepeatLastN = 0, Seed = seed,
        },
    };

    private static Task<IOnnxCausalLmModel> Load(OnnxRunnerOptions options = null) =>
        OnnxCausalLmModel.LoadFromDirectoryAsync(
            CausalLmFixtures.TinyBundleDirectory, options, TestContext.Current.CancellationToken);

    private static async Task<IReadOnlyList<int>> Generate(
        IOnnxCausalLmModel model, string prompt, GenerationOptions options)
    {
        List<int> tokens = new List<int>();
        await foreach (GenerationUpdate update in model.GenerateAsync(
            prompt, options, TestContext.Current.CancellationToken))
        {
            tokens.AddRange(update.Tokens);
        }

        return tokens;
    }

    //One run of the graph over a prefix, with an EMPTY cache, taking the most likely token after it. It goes
    //through the RAW surface on purpose: what it proves is that the driver's step-by-step cache gives the
    //same answer as reading the whole prefix again from nothing, which is the only thing a cache is for.
    private static async Task<int> AnswerFromNothing(IReadOnlyList<int> sequence)
    {
        await using IOnnxModel graph = await OnnxModel.LoadFromDirectoryAsync(
            CausalLmFixtures.TinyBundleDirectory, "model.onnx",
            cancellationToken: TestContext.Current.CancellationToken);

        long[] ids = new long[sequence.Count];
        long[] mask = new long[sequence.Count];
        for (int i = 0; i < sequence.Count; i++)
        {
            ids[i] = sequence[i];
            mask[i] = 1;
        }

        Dictionary<string, OnnxTensor> feeds = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal)
        {
            ["input_ids"] = OnnxTensor.FromInt64(ids, 1, ids.Length),
            ["attention_mask"] = OnnxTensor.FromInt64(mask, 1, mask.Length),
        };

        OnnxTensor empty = OnnxTensor.FromFloats(Array.Empty<float>(), 1, 2, 0, 8);
        for (int layer = 0; layer < 2; layer++)
        {
            feeds["past_key_values." + layer + ".key"] = empty;
            feeds["past_key_values." + layer + ".value"] = empty;
        }

        OnnxTensor logits = (await graph.RunAsync(
            feeds, TestContext.Current.CancellationToken))["logits"];

        int vocabulary = (int)logits.Shape[logits.Shape.Count - 1];
        float[] last = new float[vocabulary];
        Array.Copy(logits.Floats, (int)(logits.Count - vocabulary), last, 0, vocabulary);
        return CausalLmSampler.ArgMax(last);
    }
}
