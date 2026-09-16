using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Drives <see cref="FakeRunningModel"/> through the contract, which is the check that
/// <see cref="IRunningModel"/> really is implementable by an application with no engine behind it.
/// </summary>
public sealed class FakeRunningModelTests
{
    /// <summary>A scripted reply arrives as several deltas and one final update.</summary>
    [Fact]
    public async Task ChatAsync_streams_the_scripted_reply_as_deltas()
    {
        //Arrange
        using FakeRunningModel model = new FakeRunningModel("the answer is four");
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "what is two plus two?"));

        //Act
        List<ChatUpdate> updates = new List<ChatUpdate>();
        await foreach (ChatUpdate update in model.ChatAsync(request, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        //Assert
        updates.Should().HaveCount(5);
        updates[updates.Count - 1].IsFinal.Should().BeTrue();
        updates[updates.Count - 1].FinishReason.Should().Be(FinishReason.Stop);
        updates[updates.Count - 1].Statistics.GeneratedTokens.Should().Be(4);

        StringBuilder content = new StringBuilder();
        foreach (ChatUpdate update in updates) content.Append(update.ContentDelta);
        content.ToString().Should().Be("the answer is four");
    }

    /// <summary>The whole-reply member aggregates what the streaming one produced.</summary>
    [Fact]
    public async Task ChatToEndAsync_aggregates_the_stream()
    {
        //Arrange
        using FakeRunningModel model = new FakeRunningModel("hello there");
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.System, "be brief"));
        request.Messages.Add(new ChatMessage(ChatRole.User, "hi"));

        //Act
        ChatResponse response = await model.ChatToEndAsync(request, TestContext.Current.CancellationToken);

        //Assert
        response.Message.Content.Should().Be("hello there");
        response.Message.Role.Should().Be(ChatRole.Assistant);
        response.FinishReason.Should().Be(FinishReason.Stop);
        response.Statistics.GeneratedTokens.Should().Be(2);
    }

    /// <summary>The rendered prompt shows the whole conversation, in order.</summary>
    [Fact]
    public async Task RenderChatPromptAsync_renders_every_message()
    {
        //Arrange
        using FakeRunningModel model = new FakeRunningModel("ignored");
        ChatRequest request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.System, "rules"));
        request.Messages.Add(new ChatMessage(ChatRole.User, "question"));

        //Act
        string prompt = await model.RenderChatPromptAsync(request, TestContext.Current.CancellationToken);

        //Assert
        prompt.Should().StartWith("System: rules");
        prompt.Should().Contain("User: question");
    }

    /// <summary>The token limit is honoured, which is the one generation control a fake has to respect.</summary>
    [Fact]
    public async Task GenerateToEndAsync_honours_the_token_limit()
    {
        //Arrange
        using FakeRunningModel model = new FakeRunningModel("one two three four five");
        GenerationOptions options = new GenerationOptions { MaxTokens = 2 };

        //Act
        GenerationResult result = await model.GenerateToEndAsync(
            "prompt", options, TestContext.Current.CancellationToken);

        //Assert
        result.Text.Should().Be("one two");
        result.Statistics.GeneratedTokens.Should().Be(2);
    }

    /// <summary>The members that only record what they were told record it.</summary>
    [Fact]
    public async Task The_bookkeeping_members_record_what_they_were_given()
    {
        //Arrange
        FakeRunningModel model = new FakeRunningModel("x");

        //Act
        await model.ClearCacheAsync(TestContext.Current.CancellationToken);
        await model.SetLoraAdaptersAsync(
            new[] { new LoraAdapterOptions { Path = "a.gguf", Scale = 0.5f } },
            TestContext.Current.CancellationToken);
        EmbeddingResult embeddings = await model.EmbedAsync(
            new[] { "one", "two" }, TestContext.Current.CancellationToken);
        await model.DisposeAsync();

        //Assert
        model.ClearCacheCalls.Should().Be(1);
        model.Adapters.Should().HaveCount(1);
        model.Adapters[0].Scale.Should().Be(0.5f);
        embeddings.Embeddings.Should().HaveCount(2);
        embeddings.Dimensions.Should().Be(4);
        embeddings.PromptTokens.Should().Be(6);
        model.IsDisposed.Should().BeTrue();
    }

    /// <summary>Tokenizing and detokenizing round trip.</summary>
    [Fact]
    public async Task TokenizeAsync_and_DetokenizeAsync_round_trip()
    {
        //Arrange
        using FakeRunningModel model = new FakeRunningModel();

        //Act
        IReadOnlyList<int> tokens = await model.TokenizeAsync(
            "round trip", cancellationToken: TestContext.Current.CancellationToken);
        string text = await model.DetokenizeAsync(tokens, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        tokens.Should().HaveCount(10);
        text.Should().Be("round trip");
    }
}
