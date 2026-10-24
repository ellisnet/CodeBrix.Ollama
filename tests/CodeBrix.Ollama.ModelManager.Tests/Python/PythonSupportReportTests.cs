using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what a <see cref="PythonSupportReport"/> is: a description that answers for every module it was
/// asked about, never hands out a null list, and calls itself usable only when all three of its
/// conditions hold.
/// </summary>
public sealed class PythonSupportReportTests
{
    private static readonly Version Supported = new Version(3, 13, 5);

    [Fact]
    public void IsUsable_is_true_when_the_library_loads_and_the_version_is_supported()
        => Build(true, Supported, true).IsUsable.Should().BeTrue();

    [Fact]
    public void IsUsable_is_false_when_the_library_does_not_load()
        => Build(false, Supported, true).IsUsable.Should().BeFalse();

    [Fact]
    public void IsUsable_is_false_when_the_version_is_not_supported()
        => Build(true, new Version(3, 8), false).IsUsable.Should().BeFalse();

    [Fact]
    public void IsUsable_is_false_when_one_module_is_missing()
    {
        //Arrange
        var modules = new[]
        {
            new PythonModuleReport("sys", true, null),
            new PythonModuleReport("onnx", false, "No module named 'onnx'"),
        };

        //Act
        PythonSupportReport report = Build(true, Supported, true, modules);

        //Assert
        report.IsUsable.Should().BeFalse();
        report.Modules.Should().HaveCount(2);
        report.Modules[1].Error.Should().Be("No module named 'onnx'");
    }

    [Fact]
    public void IsUsable_is_false_when_one_module_is_older_than_its_floor()
    {
        //Arrange
        var modules = new[]
        {
            new PythonModuleReport("onnx", true, null, "1.22.0", "1.16", true),
            new PythonModuleReport("torch", true, null, "2.5.1+cpu", "2.6", false),
        };

        //Act
        PythonSupportReport report = Build(true, Supported, true, modules);

        //Assert
        report.IsUsable.Should().BeFalse();
        report.Modules[1].InstalledVersion.Should().Be("2.5.1+cpu");
        report.Modules[1].MinimumVersion.Should().Be("2.6");
    }

    [Fact]
    public void IsUsable_is_true_when_every_module_meets_its_floor()
    {
        //Arrange
        var modules = new[]
        {
            new PythonModuleReport("onnx", true, null, "1.22.0", "1.16", true),
            new PythonModuleReport("numpy", true, null, "2.5.3", null, true),
        };

        //Act and assert
        Build(true, Supported, true, modules).IsUsable.Should().BeTrue();
    }

    [Fact]
    public void a_module_report_made_without_a_floor_meets_it()
    {
        //Act
        var module = new PythonModuleReport("sys", true, null);

        //Assert
        module.MinimumVersion.Should().BeNull();
        module.InstalledVersion.Should().BeNull();
        module.MeetsMinimumVersion.Should().BeTrue();
    }

    [Fact]
    public void Modules_is_never_null()
        => Build(true, Supported, true, null).Modules.Should().BeEmpty();

    [Fact]
    public void Problems_is_never_null()
    {
        //Arrange
        var report = new PythonSupportReport(
            null, PythonLibrarySource.NotFound, false, null, false, null,
            PythonVirtualEnvironmentSource.None, false, PythonEngineOwner.None, null, null);

        //Act and assert
        report.Problems.Should().BeEmpty();
        report.Modules.Should().BeEmpty();
        report.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void every_value_it_is_given_is_the_value_it_reports()
    {
        //Arrange
        var modules = new[] { new PythonModuleReport("sys", true, null) };
        var problems = new[] { "something to say" };

        //Act
        var report = new PythonSupportReport(
            "/usr/lib/libpython3.13.so",
            PythonLibrarySource.Code,
            true,
            Supported,
            true,
            "/home/someone/venvs/work",
            PythonVirtualEnvironmentSource.EnvironmentVariable,
            true,
            PythonEngineOwner.ModelManager,
            modules,
            problems);

        //Assert
        report.LibraryPath.Should().Be("/usr/lib/libpython3.13.so");
        report.LibrarySource.Should().Be(PythonLibrarySource.Code);
        report.LibraryLoads.Should().BeTrue();
        report.Version.Should().Be(Supported);
        report.IsSupportedVersion.Should().BeTrue();
        report.VirtualEnvironment.Should().Be("/home/someone/venvs/work");
        report.VirtualEnvironmentSource.Should().Be(PythonVirtualEnvironmentSource.EnvironmentVariable);
        report.IsInitialized.Should().BeTrue();
        report.Owner.Should().Be(PythonEngineOwner.ModelManager);
        report.Problems.Should().HaveCount(1);
    }

    /// <summary>
    /// Builds a report with everything but the three values under test held steady.
    /// </summary>
    /// <param name="libraryLoads">Whether the library loads.</param>
    /// <param name="version">The version to report.</param>
    /// <param name="isSupported">Whether that version is supported.</param>
    /// <param name="modules">The module reports, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    private static PythonSupportReport Build(
        bool libraryLoads, Version version, bool isSupported, IReadOnlyList<PythonModuleReport> modules = null)
        => new PythonSupportReport(
            "/usr/lib/libpython3.13.so",
            PythonLibrarySource.Code,
            libraryLoads,
            version,
            isSupported,
            null,
            PythonVirtualEnvironmentSource.None,
            false,
            PythonEngineOwner.None,
            modules,
            Array.Empty<string>());
}
