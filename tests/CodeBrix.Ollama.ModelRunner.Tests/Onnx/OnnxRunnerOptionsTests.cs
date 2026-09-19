using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>The defaults a caller gets when it sets nothing.</summary>
public sealed class OnnxRunnerOptionsTests
{
    /// <summary>The thread count is left to the engine unless a caller states one.</summary>
    [Fact]
    public void Threads_defaults_to_the_engine() => new OnnxRunnerOptions().Threads.Should().BeNull();

    /// <summary>There is no cap on the engine's own choice unless a caller sets one.</summary>
    [Fact]
    public void MaxThreads_defaults_to_no_cap() => new OnnxRunnerOptions().MaxThreads.Should().BeNull();

    /// <summary>The widest kernels the processor offers are the default.</summary>
    [Fact]
    public void KernelPath_defaults_to_automatic() =>
        new OnnxRunnerOptions().KernelPath.Should().Be(OnnxKernelPath.Automatic);

    /// <summary>Buffers are reused by default, which is what keeps a decode step's allocation flat.</summary>
    [Fact]
    public void ReuseBuffers_defaults_to_true() => new OnnxRunnerOptions().ReuseBuffers.Should().BeTrue();

    /// <summary>A copy carries every setting and is independent of the original.</summary>
    [Fact]
    public void Copy_carries_every_setting()
    {
        //Arrange
        var options = new OnnxRunnerOptions
        {
            Threads = 3, MaxThreads = 5, KernelPath = OnnxKernelPath.Scalar, ReuseBuffers = false,
        };

        //Act
        var copy = options.Copy();
        options.Threads = 9;
        options.MaxThreads = 11;

        //Assert
        copy.Threads.Should().Be(3);
        copy.MaxThreads.Should().Be(5);
        copy.KernelPath.Should().Be(OnnxKernelPath.Scalar);
        copy.ReuseBuffers.Should().BeFalse();
    }
}
