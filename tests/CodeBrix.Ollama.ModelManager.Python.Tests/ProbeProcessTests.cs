using System;
using System.Collections.Generic;
using System.Threading;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// The three ownership observations that only a whole process can make: this library starting an
/// interpreter and ending it, this library finding one the host already started, and a process that never
/// shuts one down still ending by itself.
/// </summary>
/// <remarks>
/// Each runs the probe console application once, with the virtual environment handed to the child
/// outright. The child is always killed before the helper returns, so a probe that hangs can never hang
/// this run.
/// </remarks>
public sealed class ProbeProcessTests
{
    /// <summary>How long a probe that shuts its own interpreter down is given to end.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(120);

    /// <summary>The assembly's one interpreter, taken so that the gate and the environment agree.</summary>
    private readonly PythonTestFixture _fixture;

    /// <summary>
    /// Takes the assembly's interpreter.
    /// </summary>
    /// <param name="fixture">The assembly fixture.</param>
    public ProbeProcessTests(PythonTestFixture fixture) => _fixture = fixture;

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void the_own_mode_starts_an_interpreter_ends_it_and_refuses_python_afterwards()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("own", ExitWait, ChildEnvironment(), cancellationToken);

        //Assert
        run.Exited.Should().BeTrue("the probe must end by itself." + run.Report());
        run.ExitCode.Should().Be(0, run.Report());
        run.Printed("owner: ModelManager").Should().BeTrue(run.Report());
        run.Printed("usable: True").Should().BeTrue(run.Report());
        run.Printed("problems: 0").Should().BeTrue(run.Report());
        run.Printed("initialized-after-shutdown: False").Should().BeTrue(run.Report());
        run.Printed("owner-after-shutdown: ModelManager").Should().BeTrue(run.Report());
        run.Output.Should().Contain("after-shutdown: Python has been shut down for this process");
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void the_hostowned_mode_leaves_the_hosts_interpreter_alone()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("hostowned", ExitWait, ChildEnvironment(), cancellationToken);

        //Assert
        run.Exited.Should().BeTrue("the probe must end by itself." + run.Report());
        run.ExitCode.Should().Be(0, run.Report());
        run.Printed("host-initialized: True").Should().BeTrue(run.Report());
        run.Printed("owner: Host").Should().BeTrue(run.Report());
        run.Printed("usable: True").Should().BeTrue(run.Report());
        run.Printed("still-initialized: True").Should().BeTrue(
            "this library must never shut down an interpreter it did not start." + run.Report());
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void the_noshutdown_mode_still_lets_the_process_end()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("noshutdown", ExitWait, ChildEnvironment(), cancellationToken);

        //Assert
        run.Exited.Should().BeTrue(
            "the bounded process-exit mode is the safety net for a consumer who never disposes anything."
            + run.Report());
        run.ExitCode.Should().Be(0, run.Report());
        run.Printed("owner: ModelManager").Should().BeTrue(run.Report());
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void the_hostcode_mode_reports_the_library_the_host_named_in_code()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("hostcode", ExitWait, ChildEnvironment(), cancellationToken);

        //Assert
        run.Exited.Should().BeTrue("the probe must end by itself." + run.Report());
        run.ExitCode.Should().Be(0, run.Report());
        run.Printed("host-initialized: True").Should().BeTrue(run.Report());
        run.Printed("owner: Host").Should().BeTrue(run.Report());
        run.Printed("library-source: Host").Should().BeTrue(
            "nothing this library reads named that library, so it can only have come from the host."
            + run.Report());
        run.Printed("library-path-set: True").Should().BeTrue(
            "the running interpreter states its own library even when nothing here resolved one."
            + run.Report());
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void the_reduce_mode_quantizes_a_real_graph_and_still_ends()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var directory = new TempExportDirectory();
        string graph = System.IO.Path.Combine(directory.DirectoryPath, "probe.onnx");
        VenvPythonRun written = VenvPython.Run(
            _fixture.VirtualEnvironment,
            "make_probe_model.py",
            new[] { graph },
            TimeSpan.FromMinutes(5),
            cancellationToken);
        written.ExitCode.Should().Be(0, written.Report());

        Dictionary<string, string> environment = ChildEnvironment();
        environment[ProbeProcess.ModelVariable] = graph;

        //Act
        ProbeRun run = ProbeProcess.Run("reduce", ExitWait, environment, cancellationToken);

        //Assert
        run.Exited.Should().BeTrue(
            "a process that has run a quantizer must still end by itself." + run.Report());
        run.ExitCode.Should().Be(0, run.Report());
        run.Printed("engine: Python").Should().BeTrue(run.Report());
        run.Printed("mode: WeightOnlyInt4").Should().BeTrue(run.Report());
        run.Printed("name: local/probe/reduce:onnx-int4-weights").Should().BeTrue(run.Report());
        run.Printed("files: 2").Should().BeTrue(
            "the configuration beside the graph is carried through with it." + run.Report());
        run.Printed("owner: ModelManager").Should().BeTrue(run.Report());
        run.Printed("initialized-after-shutdown: False").Should().BeTrue(run.Report());
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void a_mode_the_probe_does_not_know_is_refused()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        //Act
        ProbeRun run = ProbeProcess.Run("nonsense", ExitWait, ChildEnvironment(), cancellationToken);

        //Assert
        run.Exited.Should().BeTrue(run.Report());
        run.ExitCode.Should().Be(2, run.Report());
    }

    /// <summary>
    /// The environment every probe is handed: the virtual environment this assembly's own interpreter
    /// uses, named outright so the child does not depend on how the run was launched.
    /// </summary>
    /// <returns>The variables to set for the child.</returns>
    private Dictionary<string, string> ChildEnvironment()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TestGates.PythonVirtualEnvironment] = _fixture.VirtualEnvironment,
        };
}
