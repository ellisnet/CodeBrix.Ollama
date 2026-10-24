using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the parts of the boundary class that can be exercised without an interpreter: the supported
/// range it reads from the embedding layer, the refusal it issues after a shutdown, and the way it reads
/// a module name out of what Python says when an import fails.
/// </summary>
public sealed class PythonHostTests
{
    [Theory]
    [InlineData("No module named 'onnxruntime'", "onnxruntime")]
    [InlineData("ModuleNotFoundError : No module named 'onnx_ir'", "onnx_ir")]
    [InlineData("No module named 'a.b'", "a.b")]
    [InlineData("ValueError: something else entirely", null)]
    [InlineData("No module named ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FindMissingModuleName_reads_the_module_out_of_what_python_said(string message, string expected)
        => PythonHost.FindMissingModuleName(message).Should().Be(expected);

    [Fact]
    public void ShutDownMessage_says_that_it_cannot_be_started_again()
    {
        //Act and assert
        PythonHost.ShutDownMessage.Should().Contain("has been shut down for this process");
        PythonHost.ShutDownMessage.Should().Contain("cannot be initialized again");
    }

    [Fact]
    public void the_supported_range_is_the_one_the_embedding_layer_declares()
    {
        //Act and assert
        PythonHost.MinimumSupportedVersion.Should().NotBeNull();
        PythonHost.MaximumSupportedVersion.Should().NotBeNull();
        (PythonHost.MaximumSupportedVersion > PythonHost.MinimumSupportedVersion).Should().BeTrue();
    }

    [Fact]
    public void IsShutDown_is_false_in_a_suite_that_never_starts_an_interpreter()
        => PythonHost.IsShutDown.Should().BeFalse();

    [Theory]
    [InlineData(PythonLibrarySource.NotFound, "/usr/lib/libpython3.13.so", PythonLibrarySource.Host)]
    [InlineData(PythonLibrarySource.NotFound, null, PythonLibrarySource.NotFound)]
    [InlineData(PythonLibrarySource.NotFound, "   ", PythonLibrarySource.NotFound)]
    [InlineData(PythonLibrarySource.Code, "/usr/lib/libpython3.13.so", PythonLibrarySource.Code)]
    [InlineData(PythonLibrarySource.EnvironmentVariable, "/usr/lib/libpython3.13.so",
        PythonLibrarySource.EnvironmentVariable)]
    [InlineData(PythonLibrarySource.VirtualEnvironment, "/usr/lib/libpython3.13.so",
        PythonLibrarySource.VirtualEnvironment)]
    public void DescribeRunningLibrarySource_says_Host_only_for_a_path_nothing_here_resolved(
        PythonLibrarySource resolved, string runningPath, PythonLibrarySource expected)
        => PythonHost.DescribeRunningLibrarySource(resolved, runningPath).Should().Be(expected);

    [Theory]
    [InlineData("torch", "2.5.1+cpu", "2.6", false)]
    [InlineData("torch", "2.14.0+cpu", "2.6", true)]
    [InlineData("onnx", "1.15.0", "1.16", false)]
    [InlineData("onnx", "1.17.0.dev0", "1.16", true)]
    [InlineData("transformers", "4.57.6", "5.10", false)]
    [InlineData("transformers", "5.10.0", "5.10", true)]
    [InlineData("torch", null, "2.6", false)]
    public void DescribeImported_measures_a_module_with_a_floor_against_it(
        string module, string installed, string minimum, bool meets)
    {
        //Act
        PythonModuleReport report = PythonHost.DescribeImported(module, installed);

        //Assert
        report.IsInstalled.Should().BeTrue();
        report.InstalledVersion.Should().Be(installed);
        report.MinimumVersion.Should().Be(minimum);
        report.MeetsMinimumVersion.Should().Be(meets);
    }

    [Fact]
    public void DescribeImported_holds_a_module_with_no_floor_to_nothing()
    {
        //Act
        PythonModuleReport report = PythonHost.DescribeImported("numpy", "0.1");

        //Assert
        report.InstalledVersion.Should().Be("0.1");
        report.MinimumVersion.Should().BeNull();
        report.MeetsMinimumVersion.Should().BeTrue();
    }

    [Fact]
    public void DescribeTooOld_names_the_module_both_versions_and_the_advisory()
    {
        //Arrange
        PythonModuleReport report = PythonHost.DescribeImported("torch", "2.5.1");

        //Act
        string problem = PythonHost.DescribeTooOld(report, "/usr/lib/libpython3.13.so");

        //Assert
        problem.Should().StartWith("The Python module 'torch' in the CPython at /usr/lib/libpython3.13.so");
        problem.Should().Contain("is version 2.5.1, and at least 2.6 is required");
        problem.Should().Contain("CVE-2025-32434");
    }
}
