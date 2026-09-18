using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// What the boundary class does with a real interpreter: running the one script that ships today,
/// converting what it leaves behind, and refusing a script that is not in the package.
/// </summary>
public sealed class PythonHostTests
{
    /// <summary>The assembly's one interpreter.</summary>
    private readonly PythonTestFixture _fixture;

    /// <summary>
    /// Takes the assembly's interpreter.
    /// </summary>
    /// <param name="fixture">The assembly fixture.</param>
    public PythonHostTests(PythonTestFixture fixture) => _fixture = fixture;

    [EnvGatedFact(TestGates.RunPythonTests)]
    public async Task RunScriptAsync_of_the_version_probe_reports_the_virtual_environments_prefix()
    {
        //Act
        IReadOnlyDictionary<string, object> values = await PythonHost.RunScriptAsync(
            _fixture.Options,
            "the version probe",
            PythonScripts.ProbeVersion,
            null,
            new[] { "sys" },
            TestContext.Current.CancellationToken);

        //Assert
        values["prefix"].Should().Be(_fixture.VirtualEnvironment);
        values["base_prefix"].Should().NotBe(_fixture.VirtualEnvironment);
        values["major"].Should().Be(3L);
        ((string)values["executable"]).StartsWith(_fixture.VirtualEnvironment, StringComparison.Ordinal)
            .Should().BeTrue((string)values["executable"]);
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public async Task RunScriptAsync_reports_the_same_version_the_report_carries()
    {
        //Act
        IReadOnlyDictionary<string, object> values = await PythonHost.RunScriptAsync(
            _fixture.Options,
            "the version probe",
            PythonScripts.ProbeVersion,
            null,
            new[] { "sys" },
            TestContext.Current.CancellationToken);

        //Assert
        var reported = new Version(
            (int)(long)values["major"], (int)(long)values["minor"], (int)(long)values["micro"]);
        reported.Should().Be(_fixture.Report.Version);
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public async Task RunScriptAsync_accepts_parameters_a_script_does_not_use()
    {
        //Arrange
        var parameters = new Dictionary<string, object>
        {
            ["modelName"] = "hf.co/skytnt/midi-model-tv2o-medium",
            ["blockSize"] = 128,
            ["preprocess"] = true,
        };

        //Act
        IReadOnlyDictionary<string, object> values = await PythonHost.RunScriptAsync(
            _fixture.Options,
            "the version probe",
            PythonScripts.ProbeVersion,
            parameters,
            new[] { "sys" },
            TestContext.Current.CancellationToken);

        //Assert
        values["prefix"].Should().Be(_fixture.VirtualEnvironment);
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public async Task RunScriptAsync_with_a_script_that_ships_in_no_package_throws()
    {
        //Arrange
        Func<Task> act = async () => await PythonHost.RunScriptAsync(
            _fixture.Options,
            "a feature that does not exist",
            "not_a_script.py",
            null,
            Array.Empty<string>(),
            TestContext.Current.CancellationToken);

        //Act and assert
        ModelManagerException thrown = (await act.Should().ThrowAsync<ModelManagerException>()).Which;
        thrown.Message.Should().Contain("not_a_script.py");
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public async Task RunScriptAsync_with_a_module_that_is_not_installed_throws_the_friendly_exception()
    {
        //Arrange
        Func<Task> act = async () => await PythonHost.RunScriptAsync(
            _fixture.Options,
            "exporting to ONNX",
            PythonScripts.ProbeVersion,
            null,
            new[] { "codebrix_not_a_module" },
            TestContext.Current.CancellationToken);

        //Act and assert
        PythonModuleNotInstalledException thrown =
            (await act.Should().ThrowAsync<PythonModuleNotInstalledException>()).Which;
        thrown.ModuleName.Should().Be("codebrix_not_a_module");
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void the_library_this_machine_runs_is_inside_the_supported_range()
    {
        //Act and assert
        PythonHost.MinimumSupportedVersion.Should().NotBeNull();
        (_fixture.Report.Version >= PythonHost.MinimumSupportedVersion).Should().BeTrue();
        (_fixture.Report.Version <= PythonHost.MaximumSupportedVersion).Should().BeTrue();
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void the_version_probe_script_only_imports_what_the_allow_list_names()
        => PythonScriptImports.TryFindForbiddenImport(
            PythonScripts.Read(PythonScripts.ProbeVersion), out _).Should().BeFalse();

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void IsShutDown_is_false_while_the_assemblys_interpreter_is_still_running()
        => PythonHost.IsShutDown.Should().BeFalse();
}
