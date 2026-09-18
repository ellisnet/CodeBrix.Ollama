using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// What <see cref="PythonSupport"/> reports when there really is a CPython to report on: the version, the
/// virtual environment the modules live in, which modules import, and the two friendly exceptions.
/// </summary>
/// <remarks>
/// Every test here shares the assembly's one interpreter, which the fixture started with the same call a
/// feature would make. Nothing here shuts it down: that is the fixture's job, once, at the end, and the
/// refusal that follows a shutdown is watched from a child process instead.
/// </remarks>
public sealed class PythonSupportTests
{
    /// <summary>The assembly's one interpreter.</summary>
    private readonly PythonTestFixture _fixture;

    /// <summary>
    /// Takes the assembly's interpreter.
    /// </summary>
    /// <param name="fixture">The assembly fixture.</param>
    public PythonSupportTests(PythonTestFixture fixture) => _fixture = fixture;

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Check_reports_a_supported_version_of_python_three()
    {
        //Act
        PythonSupportReport report = _fixture.Report;

        //Assert
        report.Version.Should().NotBeNull();
        report.Version.Major.Should().Be(3);
        report.Version.Minor.Should().BeGreaterThan(9);
        report.IsSupportedVersion.Should().BeTrue();
        report.LibraryLoads.Should().BeTrue();
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Check_reports_the_virtual_environment_the_environment_variable_names()
    {
        //Act
        PythonSupportReport report = _fixture.Report;

        //Assert
        report.VirtualEnvironment.Should().Be(_fixture.VirtualEnvironment);
        report.VirtualEnvironmentSource.Should().Be(PythonVirtualEnvironmentSource.EnvironmentVariable);
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Check_reports_the_library_the_interpreter_really_loaded()
    {
        //Act
        PythonSupportReport report = _fixture.Report;

        //Assert
        report.LibraryPath.Should().NotBeEmpty();
        File.Exists(report.LibraryPath).Should().BeTrue();
        report.LibrarySource.Should().Be(PythonLibrarySource.VirtualEnvironment);
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Check_says_nothing_is_wrong_when_nothing_is()
    {
        //Act
        PythonSupportReport report = _fixture.Report;

        //Assert
        report.Problems.Should().BeEmpty();
        report.IsUsable.Should().BeTrue();
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Check_finds_the_modules_the_later_phases_need()
    {
        //Act
        PythonSupportReport report = PythonSupport.Check(
            _fixture.Options, "onnx", "onnxruntime", "torch", "transformers");

        //Assert
        report.Modules.Should().HaveCount(4);
        foreach (PythonModuleReport module in report.Modules)
        {
            module.IsInstalled.Should().BeTrue(module.Name + ": " + module.Error);
            module.Error.Should().BeNull();
        }
        report.IsUsable.Should().BeTrue();
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Check_reports_a_module_that_is_not_there_with_what_python_said()
    {
        //Act
        PythonSupportReport report = PythonSupport.Check(_fixture.Options, "sys", "codebrix_not_a_module");

        //Assert
        report.Modules.Should().HaveCount(2);
        report.Modules[0].IsInstalled.Should().BeTrue();
        report.Modules[1].IsInstalled.Should().BeFalse();
        report.Modules[1].Error.Should().Contain("No module named 'codebrix_not_a_module'");
        report.IsUsable.Should().BeFalse();
        string.Join(" ", report.Problems).Should().Contain("codebrix_not_a_module");
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Check_asks_about_a_repeated_module_once()
        => PythonSupport.Check(_fixture.Options, "sys", "sys").Modules.Should().HaveCount(1);

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Require_with_a_module_that_is_not_installed_names_the_command_that_installs_it()
    {
        //Arrange
        Action act = () => PythonSupport.Require(
            _fixture.Options, "exporting to ONNX", "codebrix_not_a_module");

        //Act
        PythonModuleNotInstalledException thrown =
            act.Should().Throw<PythonModuleNotInstalledException>().Which;

        //Assert
        thrown.ModuleName.Should().Be("codebrix_not_a_module");
        thrown.Feature.Should().Be("exporting to ONNX");
        thrown.Message.Should().Contain(
            PythonModuleNotInstalledException.InstallCommand(
                "codebrix_not_a_module", _fixture.VirtualEnvironment));
        thrown.Message.Should().Contain("exporting to ONNX");
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Require_with_modules_that_are_installed_says_nothing()
    {
        //Arrange
        Action act = () => PythonSupport.Require(_fixture.Options, "exporting to ONNX", "onnx", "onnxruntime");

        //Act and assert
        act.Should().NotThrow();
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void Owner_is_ModelManager_when_this_library_started_the_interpreter()
    {
        //Act and assert
        PythonSupport.Owner.Should().Be(PythonEngineOwner.ModelManager);
        _fixture.Report.Owner.Should().Be(PythonEngineOwner.ModelManager);
    }

    [EnvGatedFact(TestGates.RunPythonTests)]
    public void IsInitialized_is_true_once_a_module_has_been_asked_for()
    {
        //Act and assert
        PythonSupport.IsInitialized.Should().BeTrue();
        _fixture.Report.IsInitialized.Should().BeTrue();
    }
}
