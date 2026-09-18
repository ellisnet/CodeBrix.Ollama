using System;
using System.Threading;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// The fence for the inert dependency. CodeBrix.Ollama.ModelManager has exactly one NuGet dependency, and
/// the promise made about it is that a consumer who never uses a Python feature never loads it: every
/// CodeBrix.Python type this library names lives in one internal class, so the runtime has no reason to
/// bring the assembly in.
/// </summary>
/// <remarks>
/// Whether an assembly was loaded is a fact about a WHOLE PROCESS, and this process has already run other
/// tests, so the check runs in a child process of its own: the probe imports a folder, lists, resolves and
/// materializes it - everything a consumer does to obtain a model - and then reports what is loaded. This
/// test runs on every machine and needs no Python of any kind.
/// </remarks>
public sealed class PythonInertFenceTests
{
    /// <summary>How long the probe is given to run a whole bundle cycle and end.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(120);

    [Fact]
    public void a_whole_bundle_cycle_never_loads_the_python_assembly()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("inert", ExitWait, null, cancellationToken);

        //Assert
        run.Exited.Should().BeTrue("the probe must end by itself." + run.Report());
        run.ExitCode.Should().Be(0, "a non-zero code means the assembly was loaded." + run.Report());
        run.Printed("loaded: False").Should().BeTrue(
            "importing, listing, resolving and materializing must not load CodeBrix.Python." + run.Report());
    }

    [Fact]
    public void an_export_that_passes_the_publishers_graphs_through_never_loads_the_python_assembly()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("export", ExitWait, null, cancellationToken);

        //Assert
        run.Exited.Should().BeTrue("the probe must end by itself." + run.Report());
        run.ExitCode.Should().Be(0, "a non-zero code means the route or the assembly was wrong." + run.Report());
        run.Printed("route: PublisherOnnx").Should().BeTrue(
            "a bundle that ships ONNX is passed through, not converted." + run.Report());
        run.Printed("loaded: False").Should().BeTrue(
            "the pass-through route converts nothing, so it must start no interpreter." + run.Report());
    }

    [Fact]
    public void the_export_the_fence_runs_really_writes_a_derived_bundle()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("export", ExitWait, null, cancellationToken);

        //Assert
        run.Printed("tool: publisher").Should().BeTrue(run.Report());
        run.Printed("exported: 3").Should().BeTrue(run.Report());
        run.Printed("materialized: 3").Should().BeTrue(run.Report());
    }

    [Fact]
    public void a_managed_reduction_never_loads_the_python_assembly()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var environment = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ProbeProcess.ModelVariable] = OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"),
        };

        //Act
        ProbeRun run = ProbeProcess.Run("reduce-managed", ExitWait, environment, cancellationToken);

        //Assert
        run.Exited.Should().BeTrue("the probe must end by itself." + run.Report());
        run.ExitCode.Should().Be(0, "a non-zero code means the engine or the assembly was wrong." + run.Report());
        run.Printed("engine: Managed").Should().BeTrue(
            "the weight-only modes are this library's own work." + run.Report());
        run.Printed("loaded: False").Should().BeTrue(
            "reducing an existing graph to four-bit weights must need nothing installed." + run.Report());
    }

    [Fact]
    public void the_managed_reduction_the_fence_runs_really_makes_the_graph_smaller()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var environment = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ProbeProcess.ModelVariable] = OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"),
        };

        //Act
        ProbeRun run = ProbeProcess.Run("reduce-managed", ExitWait, environment, cancellationToken);

        //Assert
        run.Printed("mode: WeightOnlyInt4").Should().BeTrue(run.Report());
        run.Printed("name: local/probe/managed:onnx-int4-weights").Should().BeTrue(run.Report());
        run.Printed("files: 2").Should().BeTrue(
            "the configuration beside the graph is carried through with it." + run.Report());
        run.Output.Should().Contain("tool: CodeBrix.Ollama.ModelManager ");
    }

    [Fact]
    public void the_bundle_cycle_the_fence_runs_really_does_the_work()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("inert", ExitWait, null, cancellationToken);

        //Assert
        run.Printed("listed: 1").Should().BeTrue(run.Report());
        run.Printed("files: 3").Should().BeTrue(run.Report());
        run.Printed("materialized: 3").Should().BeTrue(run.Report());
    }
}
