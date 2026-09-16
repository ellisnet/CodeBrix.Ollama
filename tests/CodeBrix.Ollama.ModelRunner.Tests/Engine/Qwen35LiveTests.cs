using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The engine against the model this library was designed for: Qwen 3.5 35B-A3B at Q4_K_M, a twenty-gigabyte
/// hybrid mixture-of-experts model with a quarter-million-token vocabulary, on a processor with no
/// accelerator behind it.
/// </summary>
/// <remarks>
/// <para>
/// Gated behind <see cref="TestGates.LiveTests"/> and <see cref="TestGates.Qwen35Tests"/> together, because
/// the file is twenty gigabytes and the load takes minutes. Every test shares one loaded model through
/// <see cref="Qwen35ModelFixture"/>; no test here sets a timeout, so the class must be run without one.
/// </para>
/// <para>
/// The prompt is hand-built ChatML with an empty thinking block, which is exactly what this model's own
/// template renders when thinking is off. Building it by hand keeps this class to the engine: the chat
/// layer that renders it is tested where it lives.
/// </para>
/// </remarks>
public sealed class Qwen35LiveTests : IClassFixture<Qwen35ModelFixture>
{
    private const string ThinkingOffPrompt =
        "<|im_start|>user\nWhich planet do humans live on? Reply with exactly one word.<|im_end|>\n"
        + "<|im_start|>assistant\n<think>\n\n</think>\n\n";

    private readonly Qwen35ModelFixture fixture;
    private readonly ITestOutputHelper output;

    /// <summary>Creates the test class with its shared model and its output.</summary>
    /// <param name="fixture">The shared model.</param>
    /// <param name="output">Where the load timings and rates are written.</param>
    public Qwen35LiveTests(Qwen35ModelFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    /// <summary>A probe reads the architecture, the shape and the template of a twenty-gigabyte file in moments.</summary>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task ProbeAsync_describes_the_model_without_loading_it()
    {
        //Arrange
        string path = await fixture.PathAsync(TestContext.Current.CancellationToken);

        //Act
        ModelDetails details = await ModelRunner.ProbeAsync(path, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"description: {details.Description}");
        output.WriteLine($"parameters: {details.ParameterCount:N0}, weights: {details.WeightsSize:N0} bytes");
        output.WriteLine($"layers: {details.LayerCount}, heads: {details.HeadCount}, "
            + $"embedding: {details.EmbeddingLength}, vocabulary: {details.VocabularySize}");

        details.Architecture.Should().Be("qwen35moe");
        details.IsHybrid.Should().BeTrue();
        details.HasDecoder.Should().BeTrue();
        details.FileSize.Should().Be(Qwen35ModelFixture.Size);
        details.ChatTemplate.Should().NotBeNull();
        details.ChatTemplate.Should().Contain("tool_call");
        details.VocabularySize.Should().BeGreaterThan(200000);

        (details.WeightsSize >= 19UL * 1024UL * 1024UL * 1024UL).Should().BeTrue();
        (details.WeightsSize <= 24UL * 1024UL * 1024UL * 1024UL).Should().BeTrue();
    }

    /// <summary>The model loads, and the engine reports what it settled on.</summary>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task LoadAsync_loads_the_model_and_reports_what_it_settled_on()
    {
        //Arrange and act
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"load took {fixture.LoadDuration.TotalSeconds:F1} s");
        foreach (string line in fixture.MemoryLines) output.WriteLine(line);

        model.Details.Architecture.Should().Be("qwen35moe");
        model.Details.IsHybrid.Should().BeTrue();
        model.ChatTemplateDialect.Should().Be(ChatTemplateDialect.Jinja);

        RunningModel engine = (RunningModel)model;
        engine.ContextLength.Should().Be((int)Qwen35ModelFixture.ContextSize);
    }

    /// <summary>A hand-built ChatML prompt with thinking off gets a one-word answer out of the model.</summary>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task GenerateToEndAsync_answers_a_hand_built_chatml_prompt()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        GenerationOptions options = new GenerationOptions
        {
            MaxTokens = 32,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            ThinkingOffPrompt, options, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"answer: '{result.Text}'");
        output.WriteLine($"prompt: {result.Statistics.PromptTokens} tokens in "
            + $"{result.Statistics.PromptDuration.TotalSeconds:F2} s");
        output.WriteLine($"generated: {result.Statistics.GeneratedTokens} tokens at "
            + $"{result.Statistics.TokensPerSecond:F2} tokens/s");

        result.Text.Should().Contain("Earth");
        result.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
        result.FinishReason.Should().BeOneOf(FinishReason.Stop, FinishReason.Length);
    }

    /// <summary>With thinking off the model answers straight away, and nothing is separated as reasoning.</summary>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task ChatToEndAsync_answers_with_thinking_off()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = Ask("Reply with exactly one word: which planet do humans live on?", false, 32);

        //Act
        ChatResponse response = await model.ChatToEndAsync(request, TestContext.Current.CancellationToken);

        //Assert
        Report("thinking off", response);

        response.Message.Content.Should().Contain("Earth");
        string.IsNullOrEmpty(response.Message.Thinking).Should().BeTrue();
        response.FinishReason.Should().BeOneOf(FinishReason.Stop, FinishReason.Length);
    }

    /// <summary>With thinking on the model reasons first, and the reasoning is separated from the answer.</summary>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task ChatToEndAsync_separates_the_reasoning_when_thinking_is_on()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = Ask("Reply with exactly one word: which planet do humans live on?", true, 200);

        //Act
        ChatResponse response = await model.ChatToEndAsync(request, TestContext.Current.CancellationToken);

        //Assert
        Report("thinking on", response);

        response.Message.Thinking.Should().NotBeNull();
        response.Message.Thinking.Should().NotBeEmpty();
        response.Message.Thinking.Should().NotContain("<think>");
        response.Message.Content.Should().NotContain("</think>");
    }

    /// <summary>The tools on offer reach the prompt in the block this model's template writes for them.</summary>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task RenderChatPromptAsync_renders_the_tools_block()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = WeatherRequest(128);

        //Act
        string prompt = await model.RenderChatPromptAsync(request, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"prompt is {prompt.Length} characters");

        prompt.Should().Contain("<tools>");
        prompt.Should().Contain("get_weather");
        prompt.Should().Contain("<tool_call>");
        prompt.Should().Contain("<|im_start|>user\nWhat is the weather in Paris?<|im_end|>");
    }

    /// <summary>Offered a tool that answers the question, the model asks for it rather than guessing.</summary>
    /// <remarks>
    /// Thinking is off for this turn. The reasoning of a model this size runs to hundreds of tokens before
    /// it writes anything else, and the point of the test is the call, not the deliberation.
    /// </remarks>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task ChatToEndAsync_asks_for_a_tool_it_was_offered()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = WeatherRequest(128);

        //Act
        ChatResponse response = await model.ChatToEndAsync(request, TestContext.Current.CancellationToken);

        //Assert
        Report("tool call", response);
        foreach (ToolCall call in response.Message.ToolCalls)
        {
            output.WriteLine($"call {call.Id}: {call.Name}({call.ArgumentsJson})");
        }

        response.Message.ToolCalls.Should().HaveCount(1);
        response.Message.ToolCalls[0].Name.Should().Be("get_weather");
        response.Message.ToolCalls[0].Id.Should().Be("call_1");
        response.Message.ToolCalls[0].ArgumentsJson.Should().Contain("Paris");
        response.FinishReason.Should().Be(FinishReason.ToolCall);
    }

    /// <summary>The result of a tool the model asked for goes back in and is used in the answer.</summary>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task ChatToEndAsync_uses_a_tool_result_that_is_handed_back()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest first = WeatherRequest(128);
        ChatResponse call = await model.ChatToEndAsync(first, TestContext.Current.CancellationToken);

        call.Message.ToolCalls.Should().HaveCount(1);

        ChatRequest second = WeatherRequest(64);
        ChatMessage assistant = new ChatMessage(ChatRole.Assistant, call.Message.Content);
        assistant.ToolCalls.Add(call.Message.ToolCalls[0]);
        second.Messages.Add(assistant);
        second.Messages.Add(new ChatMessage(ChatRole.Tool, "Sunny, 21 C")
        {
            ToolCallId = call.Message.ToolCalls[0].Id,
            ToolName = "get_weather",
        });

        //Act
        ChatResponse answer = await model.ChatToEndAsync(second, TestContext.Current.CancellationToken);

        //Assert
        Report("tool result", answer);

        bool mentionsTheResult = answer.Message.Content.Contains("Sunny", StringComparison.OrdinalIgnoreCase)
                                 || answer.Message.Content.Contains("21", StringComparison.Ordinal);

        mentionsTheResult.Should().BeTrue();
    }

    /// <summary>
    /// What the CPU backend's weight repacking costs and buys on this machine, measured rather than assumed.
    /// </summary>
    /// <remarks>
    /// The shared model is loaded with the library's default, which lets the backend repack the weights into
    /// its faster layouts; the engine reports a repacked buffer of eleven gigabytes on top of the twenty-one
    /// gigabytes of mapped weights, which on a thirty-two gigabyte machine is memory the mapped file would
    /// otherwise have used. This test loads a second copy with the repacking off and runs the same short
    /// turn through it, so the trade can be seen in numbers. It changes no default.
    /// </remarks>
    [EnvGatedFact(new[] { TestGates.LiveTests, TestGates.Qwen35Tests })]
    public async Task LoadAsync_without_extra_buffer_types_is_measured_against_the_default()
    {
        //Arrange
        IRunningModel repacked = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatResponse withRepacking = await repacked.ChatToEndAsync(
            Ask(ComparisonQuestion, false, ComparisonTokens), TestContext.Current.CancellationToken);

        string path = await fixture.PathAsync(TestContext.Current.CancellationToken);
        List<string> lines = new List<string>();
        ModelRunner.SetLogHandler((level, line) => { lock (lines) { lines.Add(line); } });

        Stopwatch clock = Stopwatch.StartNew();
        IRunningModel plain;

        try
        {
            plain = await ModelRunner.LoadAsync(
                new ModelRunnerOptions
                {
                    ModelPath = path,
                    GpuLayers = 0,
                    ContextSize = Qwen35ModelFixture.ContextSize,
                    LoadMode = ModelLoadMode.MemoryMap,
                    UseExtraBufferTypes = false,
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            clock.Stop();
            ModelRunner.SetLogHandler(null);
        }

        //Act
        ChatResponse withoutRepacking;
        await using (plain)
        {
            withoutRepacking = await plain.ChatToEndAsync(
                Ask(ComparisonQuestion, false, ComparisonTokens), TestContext.Current.CancellationToken);
        }

        //Assert
        output.WriteLine("UseExtraBufferTypes = true (the default, shared fixture):");
        output.WriteLine($"  load {fixture.LoadDuration.TotalSeconds:F1} s");
        Report("  turn", withRepacking);

        output.WriteLine($"UseExtraBufferTypes = false:");
        output.WriteLine($"  load {clock.Elapsed.TotalSeconds:F1} s");
        Report("  turn", withoutRepacking);

        lock (lines)
        {
            foreach (string line in lines)
            {
                if (line.Contains("buffer size") || line.Contains("REPACK")) output.WriteLine("  " + line.TrimEnd());
            }
        }

        withRepacking.Statistics.GeneratedTokens.Should().Be(ComparisonTokens);
        withoutRepacking.Statistics.GeneratedTokens.Should().Be(ComparisonTokens);
    }

    private const string ComparisonQuestion = "Write a paragraph about the surface of the Moon.";

    private const int ComparisonTokens = 64;

    private static ChatRequest Ask(string question, bool? think, int maxTokens)
    {
        ChatRequest request = new ChatRequest { Think = think };
        request.Messages.Add(new ChatMessage(ChatRole.User, question));
        request.Options = new GenerationOptions
        {
            MaxTokens = maxTokens,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        return request;
    }

    private static ChatRequest WeatherRequest(int maxTokens)
    {
        ChatRequest request = Ask("What is the weather in Paris?", false, maxTokens);
        request.Tools.Add(new ToolDefinition
        {
            Name = "get_weather",
            Description = "Get the current weather for a city.",
            ParametersJsonSchema =
                "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\","
                + "\"description\":\"The city to report on.\"}},\"required\":[\"city\"]}",
        });

        return request;
    }

    private void Report(string label, ChatResponse response)
    {
        output.WriteLine($"{label}: content '{response.Message.Content}'");
        if (!string.IsNullOrEmpty(response.Message.Thinking))
        {
            output.WriteLine($"{label}: thinking '{response.Message.Thinking}'");
        }

        GenerationStatistics statistics = response.Statistics;
        output.WriteLine($"{label}: prompt {statistics.PromptTokens} tokens "
            + $"({statistics.CachedPromptTokens} cached) in {statistics.PromptDuration.TotalSeconds:F2} s, "
            + $"generated {statistics.GeneratedTokens} at {statistics.TokensPerSecond:F2} tokens/s, "
            + $"finish {response.FinishReason}");
    }
}
