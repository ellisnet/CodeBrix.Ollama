using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// What the managed ONNX engine really runs with: the thread count a caller's options resolve to, and the
/// arithmetic path behind <see cref="OnnxKernelPath.Automatic"/>.
/// </summary>
/// <remarks>
/// The resolution is exercised against a STATED detected count wherever the rule is what is being tested, so
/// those cases say the same thing on a four-core machine as on a hybrid laptop. The two cases that ask what
/// this machine's own default is are written so that they hold on any processor.
/// </remarks>
public sealed class OnnxExecutionSettingsTests
{
    /// <summary>The thread default is a number a caller could have asked for.</summary>
    [Fact]
    public void DefaultThreads_is_at_least_one_and_no_more_than_the_processors()
    {
        //Act
        int threads = OnnxExecutionSettings.DefaultThreads();

        //Assert
        (threads >= 1).Should().BeTrue();
        (threads <= Environment.ProcessorCount).Should().BeTrue();
    }

    /// <summary>
    /// The default is the performance-core count where the machine states one, and the physical count where
    /// it does not.
    /// </summary>
    [Fact]
    public void DefaultThreads_prefers_the_performance_cores_and_falls_back_to_the_physical_ones()
    {
        //Arrange
        int performance = EnginePerformanceCores.Count();

        //Act
        int threads = OnnxExecutionSettings.DefaultThreads();

        //Assert
        threads.Should().Be(performance > 0 ? performance : EnginePhysicalCores.Count());
    }

    /// <summary>With nothing asked for, the detected count is what the graph runs with.</summary>
    [Fact]
    public void Resolve_with_no_thread_count_takes_the_detected_one() =>
        OnnxExecutionSettings.Resolve(new OnnxRunnerOptions(), 16).Threads.Should().Be(16);

    /// <summary>A thread count the caller asked for is used exactly as it stands.</summary>
    [Fact]
    public void Resolve_uses_the_thread_count_the_options_name() =>
        OnnxExecutionSettings.Resolve(new OnnxRunnerOptions { Threads = 3 }, 16).Threads.Should().Be(3);

    /// <summary>A cap bounds the automatic count.</summary>
    [Fact]
    public void Resolve_bounds_the_automatic_count_by_the_cap() =>
        OnnxExecutionSettings.Resolve(new OnnxRunnerOptions { MaxThreads = 4 }, 16).Threads.Should().Be(4);

    /// <summary>A cap above what the machine has changes nothing.</summary>
    [Fact]
    public void Resolve_with_a_cap_above_the_detected_count_changes_nothing() =>
        OnnxExecutionSettings.Resolve(new OnnxRunnerOptions { MaxThreads = 64 }, 4).Threads.Should().Be(4);

    /// <summary>A cap of one really does mean one thread.</summary>
    [Fact]
    public void Resolve_with_a_cap_of_one_gives_one_thread() =>
        OnnxExecutionSettings.Resolve(new OnnxRunnerOptions { MaxThreads = 1 }, 16).Threads.Should().Be(1);

    /// <summary>An explicit count wins over the cap, even when it is larger than both the cap and the machine.</summary>
    [Fact]
    public void Resolve_lets_an_explicit_count_win_over_the_cap()
    {
        //Arrange
        var options = new OnnxRunnerOptions { Threads = 32, MaxThreads = 4 };

        //Act and assert
        OnnxExecutionSettings.Resolve(options, 16).Threads.Should().Be(32);
    }

    /// <summary>The kernel path is resolved alongside the threads, and Automatic is the widest there is.</summary>
    [Fact]
    public void Resolve_carries_the_kernel_path()
    {
        //Arrange
        var options = new OnnxRunnerOptions { KernelPath = OnnxKernelPath.Scalar };

        //Act and assert
        OnnxExecutionSettings.Resolve(options, 8).Kernel.Should().Be(OnnxKernelKind.Scalar);
        OnnxExecutionSettings.Resolve(new OnnxRunnerOptions(), 8).Kernel
            .Should().Be(OnnxExecutionSettings.Widest());
    }
}
