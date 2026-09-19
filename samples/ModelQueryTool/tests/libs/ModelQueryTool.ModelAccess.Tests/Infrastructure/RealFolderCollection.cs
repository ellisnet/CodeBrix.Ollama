using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// The collection the tests that act on the real folder and the real network belong to. It runs on its
/// own, because two of them at once would be two downloads of one model into one folder - which nothing
/// underneath coordinates - and because the application may be using that folder at the same time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealFolderCollection
{
    /// <summary>The name of the collection.</summary>
    public const string Name = "ModelQueryTool real folder";
}
