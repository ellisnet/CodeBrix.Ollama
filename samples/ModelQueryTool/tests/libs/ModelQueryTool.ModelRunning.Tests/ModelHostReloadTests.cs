using System;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.ModelRunning.Tests.Fakes;
using ModelQueryTool.ModelRunning.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelRunning.Tests;

/// <summary>Changing the context size, which a loaded model cannot do without being loaded again.</summary>
public class ModelHostReloadTests
{
    /// <summary>There is nothing to reload while no model is loaded.</summary>
    [Fact]
    public async Task ReloadAsync_is_refused_when_no_model_is_loaded()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();

        //Act
        Func<Task> act = () => harness.Host.ReloadAsync(4096, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>().WithMessage("*Stopped*");
        harness.Loader.Loads.Should().BeEmpty();
    }

    /// <summary>A size below the smallest one accepted is refused, and nothing is unloaded over it.</summary>
    [Fact]
    public async Task ReloadAsync_refuses_a_size_below_the_minimum_before_unloading_anything()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        FakeRunningModel loaded = harness.Model;

        //Act
        Func<Task> act = () => harness.Host.ReloadAsync(
            ModelHostOptions.MinimumContextSize - 1, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>();
        loaded.IsDisposed.Should().Be(false);
        harness.Loader.Loads.Should().HaveCount(1);
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Host.ContextSize.Should().Be(8192u);
    }

    /// <summary>A size larger than the model was trained for is refused, and nothing is unloaded over it.</summary>
    [Fact]
    public async Task ReloadAsync_refuses_a_size_above_the_trained_context_before_unloading_anything()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        harness.Loader.Prepare = model => model.Details = new ModelDetails { TrainingContextLength = 16384 };
        await harness.StartAsync(TestContext.Current.CancellationToken);
        FakeRunningModel loaded = harness.Model;

        //Act
        Func<Task> act = () => harness.Host.ReloadAsync(16385, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>().WithMessage("*16384*");
        loaded.IsDisposed.Should().Be(false);
        harness.Loader.Loads.Should().HaveCount(1);
        harness.Host.State.Should().Be(ModelHostState.Ready);
    }

    /// <summary>A reload unloads the model, loads it again at the new size, and starts a new conversation.</summary>
    [Fact]
    public async Task ReloadAsync_loads_the_model_again_at_the_new_size_with_a_new_conversation()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness(
            new ModelHostOptions { SystemPrompt = "be brief" });
        RecordingProgress progress = new RecordingProgress();
        harness.Loader.ProgressFractions.Add(0.5f);
        await harness.StartAsync(TestContext.Current.CancellationToken);
        await harness.SendAsync("first question", TestContext.Current.CancellationToken);
        FakeRunningModel first = harness.Model;

        //Act
        await harness.Host.ReloadAsync(4096, progress, TestContext.Current.CancellationToken);

        //Assert
        first.IsDisposed.Should().Be(true);
        harness.Loader.Loads.Should().HaveCount(2);
        harness.Loader.Loads[1].ContextSize.Should().Be(4096u);
        harness.Loader.Loads[1].ModelPath.Should().Be(ModelHostHarness.ModelPath);
        harness.Host.ContextSize.Should().Be(4096u);
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Host.TurnCount.Should().Be(0);
        harness.Host.SystemPrompt.Should().Be("be brief");
        progress.Fractions.Should().Equal(new[] { 0.5f });
    }

    /// <summary>When the new size will not load, the previous one is loaded again and the message says both things.</summary>
    [Fact]
    public async Task ReloadAsync_falls_back_to_the_previous_size_when_the_new_one_will_not_load()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        harness.Loader.FailNextLoad(new ModelLoadException("the cache would not fit"));

        //Act
        Func<Task> act = () => harness.Host.ReloadAsync(131072, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>().WithMessage("*131072*");
        harness.Loader.Loads.Should().HaveCount(3);
        harness.Loader.Loads[1].ContextSize.Should().Be(131072u);
        harness.Loader.Loads[2].ContextSize.Should().Be(8192u);
        harness.Host.State.Should().Be(ModelHostState.Ready);
        harness.Host.ContextSize.Should().Be(8192u);

        await harness.SendAsync("a question the model can still answer", TestContext.Current.CancellationToken);
        harness.Host.TurnCount.Should().Be(1);
    }

    /// <summary>The message of a fallback names the size that failed, the reason, and the size now running.</summary>
    [Fact]
    public async Task ReloadAsync_says_what_failed_and_what_is_running_instead()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        harness.Loader.FailNextLoad(new ModelLoadException("the cache would not fit"));

        //Act
        ModelRunningException thrown = null;

        try
        {
            await harness.Host.ReloadAsync(131072, null, TestContext.Current.CancellationToken);
        }
        catch (ModelRunningException exception)
        {
            thrown = exception;
        }

        //Assert
        thrown.Should().NotBeNull();
        thrown.Message.Should().Contain("the cache would not fit");
        thrown.Message.Should().Contain("8192");
        thrown.InnerException.Should().NotBeNull();
    }

    /// <summary>Only when the model cannot be put back either is the host left with nothing.</summary>
    [Fact]
    public async Task ReloadAsync_faults_the_host_only_when_the_fallback_fails_as_well()
    {
        //Arrange
        await using ModelHostHarness harness = new ModelHostHarness();
        await harness.StartAsync(TestContext.Current.CancellationToken);
        harness.Loader.FailNextLoad(new ModelLoadException("the cache would not fit"));
        harness.Loader.FailNextLoad(new ModelLoadException("the file has gone"));

        //Act
        Func<Task> act = () => harness.Host.ReloadAsync(131072, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunningException>().WithMessage("*No model is loaded*");
        harness.Loader.Loads.Should().HaveCount(3);
        harness.Host.State.Should().Be(ModelHostState.Faulted);

        await harness.StartAsync(TestContext.Current.CancellationToken);
        harness.Host.State.Should().Be(ModelHostState.Ready);
    }
}
