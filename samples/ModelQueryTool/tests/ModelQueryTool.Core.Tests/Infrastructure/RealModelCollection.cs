using Xunit;

namespace ModelQueryTool.Core.Tests.Infrastructure;

/// <summary>
/// The collection the tests that use the real model belong to. It runs on its own: one model of
/// this size in memory is as many as a machine has room for.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealModelCollection
{
    /// <summary>The name of the collection.</summary>
    public const string Name = "ModelQueryTool real model";
}
