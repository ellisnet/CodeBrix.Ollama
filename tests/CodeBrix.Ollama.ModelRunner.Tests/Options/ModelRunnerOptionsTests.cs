using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>The thread settings a caller gets when it states none.</summary>
/// <remarks>
/// What each one then RESOLVES to belongs to <c>ParameterMapperTests</c>; this is only that all three start
/// out unstated, which is what makes "say nothing and the library decides" the default.
/// </remarks>
public sealed class ModelRunnerOptionsTests
{
    /// <summary>The generation thread count is left to the library unless a caller states one.</summary>
    [Fact]
    public void Threads_defaults_to_the_library() => new ModelRunnerOptions().Threads.Should().BeNull();

    /// <summary>The prompt-processing thread count follows the generation one unless a caller states it.</summary>
    [Fact]
    public void BatchThreads_defaults_to_the_generation_threads() =>
        new ModelRunnerOptions().BatchThreads.Should().BeNull();

    /// <summary>There is no cap on the library's own choice unless a caller sets one.</summary>
    [Fact]
    public void MaxThreads_defaults_to_no_cap() => new ModelRunnerOptions().MaxThreads.Should().BeNull();
}
