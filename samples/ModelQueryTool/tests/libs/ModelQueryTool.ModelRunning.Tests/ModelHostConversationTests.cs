using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.ModelRunning.Tests.Fakes;
using ModelQueryTool.ModelRunning.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelRunning.Tests;

/// <summary>Holding the conversation: what is streamed, what is remembered, and what is sent back next time.</summary>
public class ModelHostConversationTests
{
    /// <summary>Reasoning and answer text arrive as different kinds, in the order the model wrote them.</summary>
    [Fact]
    public async Task SendAsync_streams_reasoning_and_answer_as_separate_kinds_in_order()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn turn = new FakeChatTurn().Thinking("first ").Thinking("second ").Saying("answer ").Saying("here");
        turn.Deltas.Add(new FakeChatDelta());
        harness.Loader.Prepare = model => model.Script(turn);
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        List<ChatTurnUpdate> pieces = await harness.SendAsync("hello", TestContext.Current.CancellationToken);

        //Assert
        pieces.Should().HaveCount(5);
        pieces[0].Kind.Should().Be(ChatTurnUpdateKind.Thinking);
        pieces[0].Text.Should().Be("first ");
        pieces[1].Kind.Should().Be(ChatTurnUpdateKind.Thinking);
        pieces[1].Text.Should().Be("second ");
        pieces[2].Kind.Should().Be(ChatTurnUpdateKind.Content);
        pieces[2].Text.Should().Be("answer ");
        pieces[3].Kind.Should().Be(ChatTurnUpdateKind.Content);
        pieces[3].Text.Should().Be("here");
        pieces[4].Kind.Should().Be(ChatTurnUpdateKind.Completed);
    }

    /// <summary>With no system prompt the first request is only what the user wrote.</summary>
    [Fact]
    public async Task SendAsync_sends_only_the_message_when_there_is_no_system_prompt()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await harness.SendAsync("hello", TestContext.Current.CancellationToken);

        //Assert
        FakeChatRecord sent = harness.Model.ChatRequests[0];
        sent.Messages.Should().HaveCount(1);
        sent.Messages[0].Role.Should().Be(ChatRole.User);
        sent.Messages[0].Content.Should().Be("hello");
    }

    /// <summary>The second turn carries the system prompt, the first exchange and the new message, in that order.</summary>
    [Fact]
    public async Task SendAsync_sends_the_whole_conversation_oldest_first()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(
            new ModelHostOptions { SystemPrompt = "be brief" });
        harness.Loader.Prepare = model => model
            .Script(new FakeChatTurn().Thinking("reasoning that is not kept").Saying("first answer"))
            .Script(FakeChatTurn.Answering("second answer"));
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await harness.SendAsync("first question", TestContext.Current.CancellationToken);
        await harness.SendAsync("second question", TestContext.Current.CancellationToken);

        //Assert
        FakeChatRecord sent = harness.Model.ChatRequests[1];
        sent.Messages.Should().HaveCount(4);
        sent.Messages[0].Role.Should().Be(ChatRole.System);
        sent.Messages[0].Content.Should().Be("be brief");
        sent.Messages[1].Role.Should().Be(ChatRole.User);
        sent.Messages[1].Content.Should().Be("first question");
        sent.Messages[2].Role.Should().Be(ChatRole.Assistant);
        sent.Messages[2].Content.Should().Be("first answer");
        sent.Messages[2].Thinking.Should().BeNull();
        sent.Messages[3].Role.Should().Be(ChatRole.User);
        sent.Messages[3].Content.Should().Be("second question");
        harness.Host.TurnCount.Should().Be(2);
    }

    /// <summary>The reasoning switch reaches the request and can be changed between turns without losing anything.</summary>
    [Fact]
    public async Task Think_reaches_the_request_and_can_be_changed_between_turns()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await harness.SendAsync("first question", TestContext.Current.CancellationToken);
        harness.Host.Think = false;
        await harness.SendAsync("second question", TestContext.Current.CancellationToken);

        //Assert
        harness.Model.ChatRequests[0].Think.Should().Be(true);
        harness.Model.ChatRequests[1].Think.Should().Be(false);
        harness.Host.TurnCount.Should().Be(2);
    }

    /// <summary>A second message while a turn is being written is refused, and reading can still be stopped early.</summary>
    [Fact]
    public async Task SendAsync_is_refused_while_a_turn_is_being_written()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn blocked = FakeChatTurn.Answering("still writing");
        blocked.BlocksUntilCancelled = true;
        harness.Loader.Prepare = model => model.Script(blocked);
        await harness.StartAsync(TestContext.Current.CancellationToken);

        IAsyncEnumerator<ChatTurnUpdate> turn = harness.Host
            .SendAsync("first question", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await turn.MoveNextAsync();

        //Act
        Action act = () => harness.Host.SendAsync("second question", TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ModelRunningException>();
        await turn.DisposeAsync();
        harness.Host.State.Should().Be(ModelHostState.Ready);
    }

    /// <summary>A reader that stops part way through leaves the host ready for the next message.</summary>
    [Fact]
    public async Task SendAsync_returns_to_ready_when_the_reader_stops_early()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn blocked = new FakeChatTurn().Saying("the first part ").Saying("and the rest");
        blocked.BlocksUntilCancelled = true;
        harness.Loader.Prepare = model => model.Script(blocked).Script(FakeChatTurn.Answering("second answer"));
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await foreach (ChatTurnUpdate update in
            harness.Host.SendAsync("first question", TestContext.Current.CancellationToken))
        {
            update.Kind.Should().Be(ChatTurnUpdateKind.Content);

            break;
        }

        //Assert
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Host.TurnCount.Should().Be(0);

        await harness.SendAsync("second question", TestContext.Current.CancellationToken);
        harness.Host.TurnCount.Should().Be(1);
    }

    /// <summary>A message before a model is loaded is refused, and the message names the state.</summary>
    [Fact]
    public async Task SendAsync_is_refused_when_no_model_is_loaded()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();

        //Act
        Action act = () => harness.Host.SendAsync("hello", TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ModelRunningException>().WithMessage("*Stopped*");
    }

    /// <summary>An empty message is refused.</summary>
    [Fact]
    public async Task SendAsync_is_refused_when_the_message_is_empty()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        Action act = () => harness.Host.SendAsync("   ", TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ModelRunningException>();
    }

    /// <summary>A cancelled turn leaves the conversation exactly as it was, so the question can be asked again.</summary>
    [Fact]
    public async Task SendAsync_leaves_the_conversation_alone_when_a_turn_is_cancelled()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn blocked = FakeChatTurn.Answering("half an answer");
        blocked.BlocksUntilCancelled = true;
        harness.Loader.Prepare = model => model
            .Script(FakeChatTurn.Answering("first answer"))
            .Script(blocked)
            .Script(FakeChatTurn.Answering("third answer"));
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync("first question", TestContext.Current.CancellationToken);

        using CancellationTokenSource cancellation = new CancellationTokenSource();
        IAsyncEnumerator<ChatTurnUpdate> turn = harness.Host
            .SendAsync("second question", cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        await turn.MoveNextAsync();

        //Act
        await cancellation.CancelAsync();

        Func<Task> drain = async () =>
        {
            while (await turn.MoveNextAsync())
            {
            }
        };

        //Assert
        await drain.Should().ThrowAsync<OperationCanceledException>();
        await turn.DisposeAsync();
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Host.TurnCount.Should().Be(1);

        await harness.SendAsync("third question", TestContext.Current.CancellationToken);
        FakeChatRecord sent = harness.Model.ChatRequests[2];
        sent.Messages.Should().HaveCount(3);
        sent.Messages[0].Content.Should().Be("first question");
        sent.Messages[1].Content.Should().Be("first answer");
        sent.Messages[2].Content.Should().Be("third question");
    }

    /// <summary>A turn that fails part way through is reported, and the conversation is left as it was.</summary>
    [Fact]
    public async Task SendAsync_leaves_the_conversation_alone_when_a_turn_fails()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn failing = FakeChatTurn.Answering("half an answer");
        failing.Failure = new InferenceException("the decode failed");
        harness.Loader.Prepare = model => model
            .Script(FakeChatTurn.Answering("first answer"))
            .Script(failing);
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync("first question", TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => harness.SendAsync("second question", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>().WithMessage("*the decode failed*");
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Host.TurnCount.Should().Be(1);
    }

    /// <summary>The last piece of a turn says how it ended and what it cost.</summary>
    [Fact]
    public async Task SendAsync_reports_how_the_turn_ended_and_what_it_cost()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn turn = FakeChatTurn.Answering("an answer");
        turn.Statistics = new GenerationStatistics { PromptTokens = 11, GeneratedTokens = 4 };
        harness.Loader.Prepare = model => model.Script(turn);
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        List<ChatTurnUpdate> pieces = await harness.SendAsync("hello", TestContext.Current.CancellationToken);

        //Assert
        ChatTurnUpdate completed = pieces[pieces.Count - 1];
        completed.Kind.Should().Be(ChatTurnUpdateKind.Completed);
        completed.Text.Should().Be("");
        completed.FinishReason.Should().Be(FinishReason.Stop);
        completed.Statistics.Should().BeSameAs(turn.Statistics);
        harness.Host.ConversationTokens.Should().Be(15);
    }

    /// <summary>A turn that ended because the context filled up says so, and its partial answer is kept.</summary>
    [Fact]
    public async Task SendAsync_keeps_the_partial_answer_when_the_context_filled_up()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        harness.Loader.Prepare = model => model
            .Script(FakeChatTurn.Answering("as far as it got").EndingWith(FinishReason.ContextFull))
            .Script(FakeChatTurn.Answering("second answer"));
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        List<ChatTurnUpdate> pieces = await harness.SendAsync("first question", TestContext.Current.CancellationToken);
        await harness.SendAsync("second question", TestContext.Current.CancellationToken);

        //Assert
        pieces[pieces.Count - 1].FinishReason.Should().Be(FinishReason.ContextFull);
        harness.Host.TurnCount.Should().Be(2);
        harness.Model.ChatRequests[1].Messages[1].Content.Should().Be("as far as it got");
    }

    /// <summary>Setting a system prompt starts a new conversation and does not load the model again.</summary>
    [Fact]
    public async Task SetSystemPrompt_starts_a_new_conversation_without_loading_the_model_again()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync("first question", TestContext.Current.CancellationToken);
        FakeRunningModel loaded = harness.Model;

        //Act
        harness.Host.SetSystemPrompt("answer in one sentence");

        //Assert
        harness.Host.SystemPrompt.Should().Be("answer in one sentence");
        harness.Host.TurnCount.Should().Be(0);
        harness.Host.ConversationTokens.Should().Be(0);
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Loader.Loads.Should().HaveCount(1);
        loaded.IsDisposed.Should().Be(false);

        await harness.SendAsync("second question", TestContext.Current.CancellationToken);
        FakeChatRecord sent = harness.Model.ChatRequests[1];
        sent.Messages.Should().HaveCount(2);
        sent.Messages[0].Role.Should().Be(ChatRole.System);
        sent.Messages[0].Content.Should().Be("answer in one sentence");
        sent.Messages[1].Content.Should().Be("second question");
    }

    /// <summary>An empty system prompt means there is none.</summary>
    [Fact]
    public async Task SetSystemPrompt_with_nothing_clears_it()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(
            new ModelHostOptions { SystemPrompt = "be brief" });
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        harness.Host.SetSystemPrompt("");
        await harness.SendAsync("hello", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.SystemPrompt.Should().BeNull();
        harness.Model.ChatRequests[0].Messages.Should().HaveCount(1);
        harness.Model.ChatRequests[0].Messages[0].Role.Should().Be(ChatRole.User);
    }

    /// <summary>Clearing the conversation forgets the turns and keeps the system prompt and the model.</summary>
    [Fact]
    public async Task ClearConversation_forgets_the_turns_and_keeps_the_system_prompt()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(
            new ModelHostOptions { SystemPrompt = "be brief" });
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync("first question", TestContext.Current.CancellationToken);

        //Act
        harness.Host.ClearConversation();

        //Assert
        harness.Host.TurnCount.Should().Be(0);
        harness.Host.ConversationTokens.Should().Be(0);
        harness.Host.SystemPrompt.Should().Be("be brief");
        harness.Loader.Loads.Should().HaveCount(1);
    }

    /// <summary>Neither the system prompt nor the conversation may be changed while a turn is being written.</summary>
    [Fact]
    public async Task the_conversation_cannot_be_changed_while_a_turn_is_being_written()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn blocked = FakeChatTurn.Answering("still writing");
        blocked.BlocksUntilCancelled = true;
        harness.Loader.Prepare = model => model.Script(blocked);
        await harness.StartAsync(TestContext.Current.CancellationToken);

        IAsyncEnumerator<ChatTurnUpdate> turn = harness.Host
            .SendAsync("first question", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await turn.MoveNextAsync();

        //Act
        Action setSystemPrompt = () => harness.Host.SetSystemPrompt("be brief");
        Action clear = () => harness.Host.ClearConversation();

        //Assert
        setSystemPrompt.Should().Throw<ModelRunningException>();
        clear.Should().Throw<ModelRunningException>();
        await turn.DisposeAsync();
    }
}
