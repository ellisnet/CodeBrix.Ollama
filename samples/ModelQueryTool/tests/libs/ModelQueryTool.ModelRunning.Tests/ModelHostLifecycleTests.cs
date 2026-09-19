using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.ModelRunning.Tests.Fakes;
using ModelQueryTool.ModelRunning.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelRunning.Tests;

/// <summary>Starting the model, stopping it again, and what the host says about itself while that happens.</summary>
public class ModelHostLifecycleTests
{
    /// <summary>A host with no model loaded reports nothing about one.</summary>
    [Fact]
    public async Task nothing_is_reported_about_a_model_before_one_is_loaded()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();

        //Assert
        harness.Host.State.Should().Be(ModelHostState.Stopped);
        harness.Host.Details.Should().BeNull();
        harness.Host.ModelPath.Should().BeNull();
        harness.Host.TrainedContextSize.Should().Be(0);
        harness.Host.TurnCount.Should().Be(0);
        harness.Host.ConversationTokens.Should().Be(0);
        harness.Host.ContextSize.Should().Be(8192u);
        harness.Host.Think.Should().Be(true);
        harness.Host.SystemPrompt.Should().BeNull();
    }

    /// <summary>A start, a turn and a stop walk through every state, and each one is announced.</summary>
    [Fact]
    public async Task a_start_a_turn_and_a_stop_announce_every_state_in_order()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync("hello", TestContext.Current.CancellationToken);
        await harness.Host.StopAsync();

        //Assert
        harness.States.Should().Equal(new[]
        {
            ModelHostState.Starting,
            ModelHostState.Ready,
            ModelHostState.Generating,
            ModelHostState.Ready,
            ModelHostState.Stopping,
            ModelHostState.Stopped,
        });
    }

    /// <summary>The options the application chose reach the model runner's own load options.</summary>
    [Fact]
    public async Task StartAsync_loads_with_the_options_this_application_chose()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(new ModelHostOptions { ContextSize = 4096 });

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        ModelRunnerOptions loaded = harness.Loader.Loads[0];
        loaded.ModelPath.Should().Be(ModelHostHarness.ModelPath);
        loaded.ContextSize.Should().Be(4096u);
        loaded.GpuLayers.Should().Be(0);
        loaded.LoadMode.Should().Be(ModelLoadMode.MemoryMap);
        loaded.UseExtraBufferTypes.Should().Be(false);
        loaded.Threads.Should().BeNull();
    }

    /// <summary>A thread count set in the options is handed to the model runner as it stands.</summary>
    [Fact]
    public async Task StartAsync_hands_over_a_thread_count_when_one_was_chosen()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(new ModelHostOptions { Threads = 6 });

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Loader.Loads[0].Threads.Should().Be(6);
    }

    /// <summary>What the engine reports while loading reaches the caller's own progress.</summary>
    [Fact]
    public async Task StartAsync_forwards_the_load_progress_to_the_caller()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        RecordingProgress progress = new RecordingProgress();
        harness.Loader.ProgressFractions.Add(0.25f);
        harness.Loader.ProgressFractions.Add(1f);

        //Act
        await harness.Host.StartAsync(ModelHostHarness.ModelPath, progress, TestContext.Current.CancellationToken);

        //Assert
        progress.Fractions.Should().Equal(new[] { 0.25f, 1f });
    }

    /// <summary>What the model file says about itself is readable once it is loaded.</summary>
    [Fact]
    public async Task StartAsync_reports_the_model_it_loaded()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Host.ModelPath.Should().Be(ModelHostHarness.ModelPath);
        harness.Host.Details.Should().NotBeNull();
        harness.Host.TrainedContextSize.Should().Be(262144);
    }

    /// <summary>A second start while a model is loaded is refused and loads nothing.</summary>
    [Fact]
    public async Task StartAsync_is_refused_when_a_model_is_already_loaded()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>();
        harness.Loader.Loads.Should().HaveCount(1);
    }

    /// <summary>A start with no path is refused before anything is loaded.</summary>
    [Fact]
    public async Task StartAsync_is_refused_without_a_path()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();

        //Act
        Func<Task> act = () => harness.Host.StartAsync("  ", null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>();
        harness.Loader.Loads.Should().BeEmpty();
    }

    /// <summary>A load that fails leaves the host stopped, so it can simply be tried again.</summary>
    [Fact]
    public async Task StartAsync_leaves_the_host_stopped_when_the_load_fails()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        harness.Loader.FailNextLoad(new ModelLoadException("there is no such file"));

        //Act
        Func<Task> act = () => harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>();
        harness.Host.State.Should().Be(ModelHostState.Stopped);
        harness.States.Should().Equal(new[] { ModelHostState.Starting, ModelHostState.Stopped });

        await harness.StartAsync(TestContext.Current.CancellationToken);
        harness.Host.State.Should().Be(ModelHostState.Ready);
    }

    /// <summary>The model runner's own exception is kept inside the one this library raises.</summary>
    [Fact]
    public async Task StartAsync_keeps_the_original_failure_as_the_inner_exception()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        ModelLoadException failure = new ModelLoadException("the context would not fit");
        harness.Loader.FailNextLoad(failure);

        //Act
        ModelRunningException thrown = null;

        try
        {
            await harness.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (ModelRunningException exception)
        {
            thrown = exception;
        }

        //Assert
        thrown.Should().NotBeNull();
        thrown.InnerException.Should().BeSameAs(failure);
        thrown.Message.Should().Contain("the context would not fit");
    }

    /// <summary>Stopping unloads the model and forgets the conversation.</summary>
    [Fact]
    public async Task StopAsync_unloads_the_model_and_forgets_the_conversation()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync("hello", TestContext.Current.CancellationToken);
        FakeRunningModel model = harness.Model;

        //Act
        await harness.Host.StopAsync();

        //Assert
        model.IsDisposed.Should().Be(true);
        harness.Host.State.Should().Be(ModelHostState.Stopped);
        harness.Host.ModelPath.Should().BeNull();
        harness.Host.Details.Should().BeNull();
        harness.Host.TurnCount.Should().Be(0);
        harness.Host.ConversationTokens.Should().Be(0);
    }

    /// <summary>Stopping a host that is already stopped does nothing at all.</summary>
    [Fact]
    public async Task StopAsync_does_nothing_when_no_model_is_loaded()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();

        //Act
        await harness.Host.StopAsync();
        await harness.Host.StopAsync();

        //Assert
        harness.Host.State.Should().Be(ModelHostState.Stopped);
        harness.States.Should().BeEmpty();
    }

    /// <summary>Stopping twice over a loaded model unloads it once and stays stopped.</summary>
    [Fact]
    public async Task StopAsync_can_be_called_again_after_it_has_stopped()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await harness.Host.StopAsync();
        await harness.Host.StopAsync();

        //Assert
        harness.States.Should().Equal(new[]
        {
            ModelHostState.Starting,
            ModelHostState.Ready,
            ModelHostState.Stopping,
            ModelHostState.Stopped,
        });
    }

    /// <summary>Stopping while a turn is being written cancels the turn and then unloads.</summary>
    [Fact]
    public async Task StopAsync_ends_a_turn_that_is_still_being_written()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn blocked = FakeChatTurn.Answering("thinking about it");
        blocked.BlocksUntilCancelled = true;
        harness.Loader.Prepare = model => model.Script(blocked);
        await harness.StartAsync(TestContext.Current.CancellationToken);

        IAsyncEnumerator<ChatTurnUpdate> turn = harness.Host
            .SendAsync("hello", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await turn.MoveNextAsync();
        harness.Host.State.Should().Be(ModelHostState.Generating);

        //Act
        Task stopping = harness.Host.StopAsync();

        Func<Task> drain = async () =>
        {
            while (await turn.MoveNextAsync())
            {
            }
        };

        //Assert
        await drain.Should().ThrowAsync<OperationCanceledException>();
        await turn.DisposeAsync();
        await stopping;
        harness.Host.State.Should().Be(ModelHostState.Stopped);
    }

    /// <summary>Disposing cancels a turn in flight, unloads the model, and may be done twice.</summary>
    [Fact]
    public async Task DisposeAsync_ends_a_turn_in_flight_and_can_be_called_twice()
    {
        //Arrange
        ModelHostHarness harness = new ModelHostHarness();
        FakeChatTurn blocked = FakeChatTurn.Answering("thinking about it");
        blocked.BlocksUntilCancelled = true;
        harness.Loader.Prepare = model => model.Script(blocked);
        await harness.StartAsync(TestContext.Current.CancellationToken);
        FakeRunningModel loaded = harness.Model;

        IAsyncEnumerator<ChatTurnUpdate> turn = harness.Host
            .SendAsync("hello", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await turn.MoveNextAsync();

        //Act
        ValueTask disposing = harness.Host.DisposeAsync();

        Func<Task> drain = async () =>
        {
            while (await turn.MoveNextAsync())
            {
            }
        };

        //Assert
        await drain.Should().ThrowAsync<OperationCanceledException>();
        await turn.DisposeAsync();
        await disposing;
        await harness.Host.DisposeAsync();
        loaded.IsDisposed.Should().Be(true);
    }

    /// <summary>Every member refuses to work once the host has been disposed.</summary>
    [Fact]
    public async Task every_member_throws_once_the_host_has_been_disposed()
    {
        //Arrange
        ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.Host.DisposeAsync();

        //Act
        Action readState = () => { ModelHostState unused = harness.Host.State; };
        Action clear = () => harness.Host.ClearConversation();
        Action setSystemPrompt = () => harness.Host.SetSystemPrompt("be brief");
        Action send = () => harness.Host.SendAsync("hello", TestContext.Current.CancellationToken);
        Func<Task> start = () => harness.StartAsync(TestContext.Current.CancellationToken);
        Func<Task> stop = () => harness.Host.StopAsync();
        Func<Task> reload = () => harness.Host.ReloadAsync(4096, null, TestContext.Current.CancellationToken);

        //Assert
        readState.Should().Throw<ObjectDisposedException>();
        clear.Should().Throw<ObjectDisposedException>();
        setSystemPrompt.Should().Throw<ObjectDisposedException>();
        send.Should().Throw<ObjectDisposedException>();
        await start.Should().ThrowAsync<ObjectDisposedException>();
        await stop.Should().ThrowAsync<ObjectDisposedException>();
        await reload.Should().ThrowAsync<ObjectDisposedException>();
    }
}
