using System;
using System.Collections.Generic;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the probe that never throws and the check that does, with no Python configured: what a report
/// says when there is no library, when the path named does not exist, and which of code and environment
/// wins when both name something.
/// </summary>
/// <remarks>
/// Every test here runs with the four Python environment variables cleared and restored afterwards, so
/// what the machine happens to have activated cannot change the outcome. None of these tests starts an
/// interpreter: no modules are ever asked for.
/// </remarks>
public sealed class PythonSupportTests
{
    private static readonly string[] PythonVariables =
    {
        PythonOptions.VirtualEnvironmentVariable,
        PythonOptions.LibraryPathVariable,
        "PYTHONNET_VENV",
        "VIRTUAL_ENV",
    };

    [Fact]
    public void Check_with_a_library_that_does_not_exist_names_the_path_and_never_throws()
        => WithPythonEnvironment(null, () =>
        {
            //Arrange
            string missing = Path.Combine(Path.GetTempPath(), "no-such-libpython3.13.so");
            var options = new PythonOptions { LibraryPath = missing };

            //Act
            PythonSupportReport report = PythonSupport.Check(options);

            //Assert
            report.LibraryPath.Should().Be(missing);
            report.LibrarySource.Should().Be(PythonLibrarySource.Code);
            report.LibraryLoads.Should().BeFalse();
            report.IsUsable.Should().BeFalse();
            report.Problems.Should().NotBeEmpty();
            string.Join(" ", report.Problems).Should().Contain(missing);
        });

    [Fact]
    public void Check_with_no_library_anywhere_reports_NotFound()
        => WithPythonEnvironment(null, () =>
        {
            //Act
            PythonSupportReport report = PythonSupport.Check(null);

            //Assert
            report.LibrarySource.Should().Be(PythonLibrarySource.NotFound);
            report.LibraryPath.Should().BeNull();
            report.LibraryLoads.Should().BeFalse();
            report.Version.Should().BeNull();
            report.IsSupportedVersion.Should().BeFalse();
            report.VirtualEnvironmentSource.Should().Be(PythonVirtualEnvironmentSource.None);
            report.IsUsable.Should().BeFalse();
            report.Problems.Should().NotBeEmpty();
        });

    [Fact]
    public void Check_with_no_modules_asks_about_no_modules_and_starts_nothing()
        => WithPythonEnvironment(null, () =>
        {
            //Act
            PythonSupportReport report = PythonSupport.Check(null);

            //Assert
            report.Modules.Should().BeEmpty();
            report.IsInitialized.Should().BeFalse();
            report.Owner.Should().Be(PythonEngineOwner.None);
        });

    [Fact]
    public void Check_prefers_the_library_path_from_code_over_the_environment_variable()
    {
        //Arrange
        string fromVariable = Path.Combine(Path.GetTempPath(), "from-variable-libpython3.13.so");
        string fromCode = Path.Combine(Path.GetTempPath(), "from-code-libpython3.13.so");
        var environment = new Dictionary<string, string> { [PythonOptions.LibraryPathVariable] = fromVariable };

        WithPythonEnvironment(environment, () =>
        {
            //Act
            PythonSupportReport report = PythonSupport.Check(new PythonOptions { LibraryPath = fromCode });

            //Assert
            report.LibraryPath.Should().Be(fromCode);
            report.LibrarySource.Should().Be(PythonLibrarySource.Code);
        });
    }

    [Fact]
    public void Check_falls_back_to_the_library_path_environment_variable()
    {
        //Arrange
        string fromVariable = Path.Combine(Path.GetTempPath(), "from-variable-libpython3.13.so");
        var environment = new Dictionary<string, string> { [PythonOptions.LibraryPathVariable] = fromVariable };

        WithPythonEnvironment(environment, () =>
        {
            //Act
            PythonSupportReport report = PythonSupport.Check(null);

            //Assert
            report.LibraryPath.Should().Be(fromVariable);
            report.LibrarySource.Should().Be(PythonLibrarySource.EnvironmentVariable);
        });
    }

    [Fact]
    public void Check_prefers_the_virtual_environment_from_code_over_the_environment_variable()
    {
        //Arrange
        var environment = new Dictionary<string, string>
        {
            [PythonOptions.VirtualEnvironmentVariable] = "/venvs/from-variable",
        };

        WithPythonEnvironment(environment, () =>
        {
            //Act
            PythonSupportReport report = PythonSupport.Check(
                new PythonOptions { VirtualEnvironment = "/venvs/from-code" });

            //Assert
            report.VirtualEnvironment.Should().Be("/venvs/from-code");
            report.VirtualEnvironmentSource.Should().Be(PythonVirtualEnvironmentSource.Code);
        });
    }

    [Fact]
    public void Check_falls_back_to_this_librarys_own_virtual_environment_variable()
    {
        //Arrange
        var environment = new Dictionary<string, string>
        {
            [PythonOptions.VirtualEnvironmentVariable] = "/venvs/from-variable",
            ["PYTHONNET_VENV"] = "/venvs/inherited",
        };

        WithPythonEnvironment(environment, () =>
        {
            //Act
            PythonSupportReport report = PythonSupport.Check(null);

            //Assert
            report.VirtualEnvironment.Should().Be("/venvs/from-variable");
            report.VirtualEnvironmentSource.Should().Be(PythonVirtualEnvironmentSource.EnvironmentVariable);
        });
    }

    [Theory]
    [InlineData("PYTHONNET_VENV")]
    [InlineData("VIRTUAL_ENV")]
    public void Check_reports_an_environment_the_process_was_launched_in_as_inherited(string variable)
    {
        //Arrange
        var environment = new Dictionary<string, string> { [variable] = "/venvs/inherited" };

        WithPythonEnvironment(environment, () =>
        {
            //Act
            PythonSupportReport report = PythonSupport.Check(null);

            //Assert
            report.VirtualEnvironment.Should().Be("/venvs/inherited");
            report.VirtualEnvironmentSource.Should().Be(PythonVirtualEnvironmentSource.Inherited);
        });
    }

    [Fact]
    public void Check_says_so_when_the_virtual_environment_is_not_there()
        => WithPythonEnvironment(null, () =>
        {
            //Arrange
            string missing = Path.Combine(Path.GetTempPath(), "no-such-venv-" + Guid.NewGuid().ToString("N"));

            //Act
            PythonSupportReport report = PythonSupport.Check(new PythonOptions { VirtualEnvironment = missing });

            //Assert
            report.VirtualEnvironment.Should().Be(missing);
            string.Join(" ", report.Problems).Should().Contain(missing);
            report.IsUsable.Should().BeFalse();
        });

    [Fact]
    public void Require_with_no_library_throws_and_names_the_feature()
        => WithPythonEnvironment(null, () =>
        {
            //Arrange
            Action act = () => PythonSupport.Require(null, "exporting to ONNX");

            //Act and assert
            PythonNotAvailableException thrown = act.Should().Throw<PythonNotAvailableException>().Which;
            thrown.Feature.Should().Be("exporting to ONNX");
            thrown.Message.Should().Contain("exporting to ONNX");
            thrown.Message.Should().Contain("No CPython shared library was found");
            thrown.Message.Should().Contain("the supported range is");
        });

    [Fact]
    public void Require_with_a_library_that_does_not_load_says_that_instead()
        => WithPythonEnvironment(null, () =>
        {
            //Arrange
            string notALibrary = Path.Combine(Path.GetTempPath(), "libpython3.13-" + Guid.NewGuid().ToString("N") + ".so");
            File.WriteAllText(notALibrary, "this is not an object file");
            try
            {
                Action act = () => PythonSupport.Require(
                    new PythonOptions { LibraryPath = notALibrary }, "reducing an ONNX model");

                //Act and assert
                PythonNotAvailableException thrown = act.Should().Throw<PythonNotAvailableException>().Which;
                thrown.Message.Should().Contain(notALibrary);
                thrown.Message.Should().Contain("could not be loaded");
                thrown.Message.Should().Contain("reducing an ONNX model");
            }
            finally
            {
                File.Delete(notALibrary);
            }
        });

    [Fact]
    public void Owner_is_None_until_something_starts_an_interpreter()
        => PythonSupport.Owner.Should().Be(PythonEngineOwner.None);

    [Fact]
    public void IsInitialized_is_false_in_a_suite_that_never_asks_for_a_module()
        => PythonSupport.IsInitialized.Should().BeFalse();

    /// <summary>
    /// Runs a test with every Python environment variable cleared, then with the ones it asked for set,
    /// and restores all of them however the test ends.
    /// </summary>
    /// <param name="overrides">The variables to set for the test, or <see langword="null"/> for none.</param>
    /// <param name="body">The test.</param>
    private static void WithPythonEnvironment(IReadOnlyDictionary<string, string> overrides, Action body)
    {
        var saved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string variable in PythonVariables)
        {
            saved[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }

        try
        {
            if (overrides != null)
            {
                foreach (KeyValuePair<string, string> variable in overrides)
                {
                    Environment.SetEnvironmentVariable(variable.Key, variable.Value);
                }
            }

            body();
        }
        finally
        {
            foreach (KeyValuePair<string, string> variable in saved)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }
        }
    }
}
