using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// THE FENCE'S FENCE. CodeBrix.Ollama.Core is compiled into no package of its own: each of the two library
/// packages carries its own copy, and an application that installs mismatched versions of them ends up with
/// one library running against the other's copy. CoreContract.Require turns that into a clear message - but
/// only if CoreContract.Revision was raised when the surface changed, and a bump is exactly the kind of step
/// that gets forgotten. This test hashes the whole non-private surface and compares it with the pair
/// recorded below, so any change at all has to be acknowledged.
/// </summary>
public sealed class CoreSurfaceTests
{
    /// <summary>The revision the hash below was recorded at.</summary>
    private const int RecordedRevision = 1;

    /// <summary>The SHA-256 of the canonical surface at <see cref="RecordedRevision"/>.</summary>
    private const string RecordedHash = "bd02281ac31a575956576ae58fd32b3d83208ae64defc56890d086d48b6f1734";

    [Fact]
    public void The_recorded_pair_is_the_revision_the_assembly_carries() =>
        CoreContract.Revision.Should().Be(
            RecordedRevision,
            "Core's surface changed: bump CoreContract.Revision and update the recorded hash in "
                + "CoreSurfaceTests.");

    [Fact]
    public void The_surface_is_the_one_the_recorded_hash_was_taken_of() =>
        CoreSurface.Hash().Should().Be(
            RecordedHash,
            "Core's surface changed: bump CoreContract.Revision and update the recorded hash in "
                + "CoreSurfaceTests. The surface now reads:\n" + CoreSurface.Describe());

    [Fact]
    public void The_surface_is_written_the_same_way_every_time_it_is_read() =>
        CoreSurface.Describe().Should().Be(CoreSurface.Describe());

    [Fact]
    public void Every_type_of_the_shared_project_is_internal() =>
        typeof(CoreContract).Assembly.GetTypes()
            .Where(type => type.Namespace == CoreSurface.Namespace && type.IsPublic)
            .Should().BeEmpty();
}
