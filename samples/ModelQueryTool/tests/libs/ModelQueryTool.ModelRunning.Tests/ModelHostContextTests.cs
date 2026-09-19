using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.ModelRunning.Tests.Fakes;
using ModelQueryTool.ModelRunning.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelRunning.Tests;

/// <summary>
/// Making room in the context: which turns are dropped, what the user is told about it, and what is refused
/// outright. The fake model counts one token per word, so every number below is exact.
/// </summary>
public class ModelHostContextTests
{
    /// <summary>A turn that fits sends the whole conversation and says nothing about the context.</summary>
    [Fact]
    public async Task SendAsync_says_nothing_about_the_context_while_the_conversation_fits()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(new ModelHostOptions { ContextSize = 40 });
        harness.Loader.Prepare = model => model.Script(FakeChatTurn.Answering(ModelHostHarness.Words("a1_", 8)));
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        List<ChatTurnUpdate> pieces = await harness.SendAsync(
            ModelHostHarness.Words("q1_", 8), TestContext.Current.CancellationToken);

        //Assert
        pieces[0].Kind.Should().Be(ChatTurnUpdateKind.Content);
        CountOf(pieces, ChatTurnUpdateKind.Notice).Should().Be(0);
    }

    /// <summary>When room is needed the oldest whole turn goes, and the turn opens with one notice saying so.</summary>
    [Fact]
    public async Task SendAsync_drops_the_oldest_whole_turn_and_opens_with_a_notice()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(new ModelHostOptions { ContextSize = 40 });
        harness.Loader.Prepare = model => model
            .Script(FakeChatTurn.Answering(ModelHostHarness.Words("a1_", 8)))
            .Script(FakeChatTurn.Answering(ModelHostHarness.Words("a2_", 8)))
            .Script(FakeChatTurn.Answering(ModelHostHarness.Words("a3_", 8)));
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync(ModelHostHarness.Words("q1_", 8), TestContext.Current.CancellationToken);
        await harness.SendAsync(ModelHostHarness.Words("q2_", 8), TestContext.Current.CancellationToken);

        //Act
        List<ChatTurnUpdate> pieces = await harness.SendAsync(
            ModelHostHarness.Words("q3_", 8), TestContext.Current.CancellationToken);

        //Assert
        pieces[0].Kind.Should().Be(ChatTurnUpdateKind.Notice);
        pieces[0].Text.Should().Contain("1 earlier turn was left out");
        CountOf(pieces, ChatTurnUpdateKind.Notice).Should().Be(1);

        FakeChatRecord sent = harness.Model.ChatRequests[2];
        sent.Messages.Should().HaveCount(3);
        sent.Messages[0].Content.Should().Be(ModelHostHarness.Words("q2_", 8));
        sent.Messages[1].Content.Should().Be(ModelHostHarness.Words("a2_", 8));
        sent.Messages[2].Content.Should().Be(ModelHostHarness.Words("q3_", 8));
        harness.Host.TurnCount.Should().Be(2);
    }

    /// <summary>Several turns go at once when one is not enough, and the system prompt is never one of them.</summary>
    [Fact]
    public async Task SendAsync_drops_as_many_turns_as_it_must_and_keeps_the_system_prompt()
    {
        //Arrange
        string systemPrompt = ModelHostHarness.Words("s", 3);
        await using ModelHostHarness harness = new ModelHostHarness(
            new ModelHostOptions { ContextSize = 64, SystemPrompt = systemPrompt });
        harness.Loader.Prepare = model =>
        {
            for (int number = 1; number <= 6; number++)
            {
                model.Script(FakeChatTurn.Answering(ModelHostHarness.Words("a" + number.ToString() + "_", 3)));
            }
        };
        await harness.StartAsync(TestContext.Current.CancellationToken);

        for (int number = 1; number <= 5; number++)
        {
            await harness.SendAsync(
                ModelHostHarness.Words("q" + number.ToString() + "_", 3), TestContext.Current.CancellationToken);
        }

        //Act
        List<ChatTurnUpdate> pieces = await harness.SendAsync(
            ModelHostHarness.Words("q6_", 20), TestContext.Current.CancellationToken);

        //Assert
        pieces[0].Kind.Should().Be(ChatTurnUpdateKind.Notice);
        pieces[0].Text.Should().Contain("3 earlier turns were left out");

        FakeChatRecord sent = harness.Model.ChatRequests[5];
        sent.Messages.Should().HaveCount(6);
        sent.Messages[0].Role.Should().Be(ChatRole.System);
        sent.Messages[0].Content.Should().Be(systemPrompt);
        sent.Messages[1].Content.Should().Be(ModelHostHarness.Words("q4_", 3));
        sent.Messages[2].Content.Should().Be(ModelHostHarness.Words("a4_", 3));
        sent.Messages[3].Content.Should().Be(ModelHostHarness.Words("q5_", 3));
        sent.Messages[4].Content.Should().Be(ModelHostHarness.Words("a5_", 3));
        sent.Messages[5].Content.Should().Be(ModelHostHarness.Words("q6_", 20));
        harness.Host.TurnCount.Should().Be(3);
    }

    /// <summary>A message too long for the context on its own is refused, and the conversation is untouched.</summary>
    [Fact]
    public async Task SendAsync_refuses_a_message_that_does_not_fit_on_its_own()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(new ModelHostOptions { ContextSize = 40 });
        harness.Loader.Prepare = model => model
            .Script(FakeChatTurn.Answering(ModelHostHarness.Words("a1_", 8)))
            .Script(FakeChatTurn.Answering(ModelHostHarness.Words("a2_", 8)));
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync(ModelHostHarness.Words("q1_", 8), TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => harness.SendAsync(
            ModelHostHarness.Words("q2_", 40), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>().WithMessage("*does not fit*");
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Host.TurnCount.Should().Be(1);
        harness.Model.ChatRequests.Should().HaveCount(1);

        await harness.SendAsync(ModelHostHarness.Words("q2_", 8), TestContext.Current.CancellationToken);
        harness.Model.ChatRequests[1].Messages[0].Content.Should().Be(ModelHostHarness.Words("q1_", 8));
    }

    /// <summary>On a large context the room kept back for the answer stops growing with it.</summary>
    [Fact]
    public async Task SendAsync_keeps_back_no_more_than_a_fixed_amount_of_a_large_context()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(new ModelHostOptions { ContextSize = 8192 });
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        List<ChatTurnUpdate> fitting = await harness.SendAsync(
            ModelHostHarness.Words("w", 7000), TestContext.Current.CancellationToken);
        harness.Host.ClearConversation();

        Func<Task> act = () => harness.SendAsync(
            ModelHostHarness.Words("w", 7200), TestContext.Current.CancellationToken);

        //Assert
        CountOf(fitting, ChatTurnUpdateKind.Notice).Should().Be(0);
        await act.Should().ThrowAsync<ModelRunningException>();
    }

    private static int CountOf(List<ChatTurnUpdate> pieces, ChatTurnUpdateKind kind)
    {
        int count = 0;

        foreach (ChatTurnUpdate piece in pieces)
        {
            if (piece.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }
}
