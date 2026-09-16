using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The chat path against a real model: SmolLM 360M, which embeds a ChatML Jinja template and is therefore
/// the smallest model that can exercise rendering, tokenizing and post-processing end to end.
/// </summary>
/// <remarks>
/// Gated behind <see cref="TestGates.LiveTests"/>. The class keeps its own
/// <see cref="SmolLmModelFixture"/>, so the model is loaded once for it and the cache-reuse test has a
/// previous turn to reuse. The two tests that need a different dialect load a second instance of the same
/// file, which costs a second or two.
/// </remarks>
public sealed class SmolLmChatLiveTests : IClassFixture<SmolLmModelFixture>
{
    private readonly SmolLmModelFixture fixture;
    private readonly ITestOutputHelper output;

    /// <summary>Creates the test class with its shared model and its output.</summary>
    /// <param name="fixture">The shared model.</param>
    /// <param name="output">Where the rendered prompts and the rates are written.</param>
    public SmolLmChatLiveTests(SmolLmModelFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    /// <summary>A system and user conversation gets a reply through the model's embedded template.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task ChatToEndAsync_replies_to_a_system_and_user_conversation()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = Conversation("Answer in one short sentence.", "What colour is the sky?");

        //Act
        ChatResponse response = await model.ChatToEndAsync(request, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"reply: '{response.Message.Content}'");
        output.WriteLine($"rate: {response.Statistics.TokensPerSecond:F2} tokens/s");

        model.ChatTemplateDialect.Should().Be(ChatTemplateDialect.Jinja);
        response.Message.Role.Should().Be(ChatRole.Assistant);
        response.Message.Content.Should().NotBeEmpty();
        response.Message.Thinking.Should().BeNull();
        response.Message.ToolCalls.Should().BeEmpty();
        response.FinishReason.Should().Be(FinishReason.Stop);
        response.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
    }

    /// <summary>The engine's own template matching renders the same conversation when it is asked to.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task ChatToEndAsync_with_the_native_dialect_replies()
    {
        //Arrange
        string path = await fixture.PathAsync(TestContext.Current.CancellationToken);

        await using IRunningModel model = await ModelRunner.LoadAsync(
            new ModelRunnerOptions
            {
                ModelPath = path,
                GpuLayers = 0,
                ContextSize = 1024,
                FlashAttention = FlashAttentionMode.Disabled,
                ChatTemplateDialect = ChatTemplateDialect.Native,
            },
            TestContext.Current.CancellationToken);

        ChatRequest request = Conversation("Answer in one short sentence.", "What colour is the sky?");

        //Act
        string prompt = await model.RenderChatPromptAsync(request, TestContext.Current.CancellationToken);
        ChatResponse response = await model.ChatToEndAsync(request, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"native prompt: '{prompt}'");
        output.WriteLine($"native reply: '{response.Message.Content}'");

        model.ChatTemplateDialect.Should().Be(ChatTemplateDialect.Native);
        prompt.Should().StartWith("<|im_start|>");
        response.Message.Content.Should().NotBeEmpty();
        response.FinishReason.Should().Be(FinishReason.Stop);
    }

    /// <summary>The rendered prompt is the ChatML the model was trained on.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task RenderChatPromptAsync_renders_chatml()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = Conversation("Be brief.", "Hello!");

        //Act
        string prompt = await model.RenderChatPromptAsync(request, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"prompt: '{prompt}'");
        prompt.Should().StartWith("<|im_start|>");
        prompt.Should().Contain("<|im_start|>user\nHello!<|im_end|>");
        prompt.Should().EndWith("<|im_start|>assistant\n");
    }

    /// <summary>A second turn of the same conversation re-uses the first turn's prompt.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task ChatToEndAsync_reuses_the_cached_prompt_on_a_second_turn()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);

        ChatRequest first = Conversation("Be brief.", "Name one colour.");
        ChatResponse firstResponse = await model.ChatToEndAsync(first, TestContext.Current.CancellationToken);

        ChatRequest second = Conversation("Be brief.", "Name one colour.");
        second.Messages.Add(new ChatMessage(ChatRole.Assistant, firstResponse.Message.Content));
        second.Messages.Add(new ChatMessage(ChatRole.User, "And another one."));
        second.Options = new GenerationOptions
        {
            MaxTokens = 24,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        ChatResponse secondResponse = await model.ChatToEndAsync(second, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"cached {secondResponse.Statistics.CachedPromptTokens} of "
            + $"{secondResponse.Statistics.PromptTokens} prompt tokens");

        firstResponse.Statistics.CachedPromptTokens.Should().Be(0);
        secondResponse.Statistics.CachedPromptTokens.Should().BeGreaterThan(0);
    }

    /// <summary>A JSON response format constrains the reply to JSON that really parses.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task ChatToEndAsync_with_the_json_response_format_returns_parseable_json()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = Conversation(
            "Reply with JSON only.", "Give me a JSON object with one key called city whose value is Paris.");

        request.ResponseFormat = ResponseFormat.Json;
        request.Options = new GenerationOptions
        {
            MaxTokens = 120,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        ChatResponse response = await model.ChatToEndAsync(request, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"json: '{response.Message.Content}'");
        using JsonDocument document = JsonDocument.Parse(response.Message.Content);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    /// <summary>A streaming chat arrives in pieces and ends with a final update carrying the statistics.</summary>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task ChatAsync_streams_updates_and_ends_with_the_statistics()
    {
        //Arrange
        IRunningModel model = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        ChatRequest request = Conversation("Be brief.", "Count from one to five.");
        request.Options = new GenerationOptions
        {
            MaxTokens = 32,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        //Act
        List<ChatUpdate> updates = new List<ChatUpdate>();
        await foreach (ChatUpdate update in model.ChatAsync(request, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        //Assert
        updates.Should().HaveCountGreaterThanOrEqualTo(2);

        ChatUpdate last = updates[updates.Count - 1];
        last.IsFinal.Should().BeTrue();
        last.Statistics.Should().NotBeNull();
        last.FinishReason.Should().BeOneOf(FinishReason.Stop, FinishReason.Length);

        for (int i = 0; i < updates.Count - 1; i++)
        {
            updates[i].IsFinal.Should().BeFalse();
            updates[i].ToolCalls.Should().BeEmpty();
            updates[i].FinishReason.Should().Be(FinishReason.None);
        }

        output.WriteLine($"{updates.Count} updates at {last.Statistics.TokensPerSecond:F2} tokens/s");
    }

    /// <summary>
    /// Ollama's own ChatML template and the model's embedded ChatML Jinja template render the same prompt.
    /// </summary>
    /// <remarks>
    /// The conversation opens with a system message because it has to: SmolLM's Jinja template writes a
    /// default system message of its own when the first message is not one, and Ollama's built-in does not,
    /// so a plain user turn is the one conversation on which the two cannot agree.
    /// </remarks>
    [EnvGatedFact(TestGates.LiveTests)]
    public async Task RenderChatPromptAsync_in_the_ollama_dialect_matches_the_jinja_dialect()
    {
        //Arrange
        IRunningModel jinja = await fixture.ModelAsync(TestContext.Current.CancellationToken);
        string path = await fixture.PathAsync(TestContext.Current.CancellationToken);

        await using IRunningModel ollama = await ModelRunner.LoadAsync(
            new ModelRunnerOptions
            {
                ModelPath = path,
                GpuLayers = 0,
                ContextSize = 512,
                FlashAttention = FlashAttentionMode.Disabled,
                OllamaTemplate = OllamaTemplate.BuiltIn("chatml").Source,
            },
            TestContext.Current.CancellationToken);

        ChatRequest request = Conversation("Be brief.", "Hello!");

        //Act
        string fromJinja = await jinja.RenderChatPromptAsync(request, TestContext.Current.CancellationToken);
        string fromOllama = await ollama.RenderChatPromptAsync(request, TestContext.Current.CancellationToken);

        //Assert
        output.WriteLine($"jinja:  '{fromJinja}'");
        output.WriteLine($"ollama: '{fromOllama}'");

        ollama.ChatTemplateDialect.Should().Be(ChatTemplateDialect.Ollama);
        fromOllama.TrimEnd().Should().Be(fromJinja.TrimEnd());
    }

    private static ChatRequest Conversation(string system, string user)
    {
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.System, system));
        request.Messages.Add(new ChatMessage(ChatRole.User, user));
        request.Options = new GenerationOptions
        {
            MaxTokens = 64,
            Sampling = new SamplingOptions { Temperature = 0f },
        };

        return request;
    }
}
