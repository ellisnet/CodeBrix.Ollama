using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the version floors a Python module is held to: which modules have one, how a version a
/// module reports is read - local labels, development and pre-release builds, post-releases - and
/// which side of a floor it lands on.
/// </summary>
public sealed class PythonModuleFloorsTests
{
    [Theory]
    [InlineData("torch", "2.6")]
    [InlineData("onnx", "1.16")]
    [InlineData("transformers", "5.10")]
    public void For_names_the_floor_of_each_module_that_has_one(string module, string expected)
        => PythonModuleFloors.Describe(PythonModuleFloors.For(module).Minimum).Should().Be(expected);

    [Theory]
    [InlineData("numpy")]
    [InlineData("onnxruntime")]
    [InlineData("Torch")]
    [InlineData(null)]
    public void For_is_null_for_a_module_with_no_floor(string module)
        => PythonModuleFloors.For(module).Should().BeNull();

    [Theory]
    [InlineData("2.6.0", true)]
    [InlineData("2.6", true)]
    [InlineData("2.6.0+cpu", true)]
    [InlineData("2.14.0+cpu", true)]
    [InlineData("2.6.1", true)]
    [InlineData("2.6.0.post1", true)]
    [InlineData("3.0.0", true)]
    [InlineData("1!0.1", true)]
    [InlineData("v2.6.0", true)]
    [InlineData("2.5.1", false)]
    [InlineData("2.5.1+cu124", false)]
    [InlineData("2.6.0.dev20241112+cpu", false)]
    [InlineData("2.6.0rc1", false)]
    [InlineData("2.6.0a0+git1234", false)]
    [InlineData("1.17.0", false)]
    [InlineData("10.0", true)]
    [InlineData("not a version", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Meets_compares_against_a_2_6_floor_the_way_python_packaging_would(string installed, bool expected)
        => PythonModuleFloors.Meets(installed, new Version(2, 6, 0)).Should().Be(expected);

    [Theory]
    [InlineData("1.17.0.dev0", true)]
    [InlineData("1.16.0", true)]
    [InlineData("1.16.0.dev0", false)]
    [InlineData("1.15.0", false)]
    [InlineData("1.22.0", true)]
    public void Meets_handles_development_builds_against_the_onnx_floor(string installed, bool expected)
        => PythonModuleFloors.Meets(installed, new Version(1, 16, 0)).Should().Be(expected);

    [Fact]
    public void Meets_holds_the_validated_transformers_below_its_floor()
        => PythonModuleFloors.Meets("4.57.6", new Version(5, 10, 0)).Should().BeFalse();

    [Theory]
    [InlineData("2.6.0+cpu", "2.6.0", false)]
    [InlineData("1.17.0.dev0", "1.17.0", true)]
    [InlineData("5.10.0rc2", "5.10.0", true)]
    [InlineData("4.57.6", "4.57.6", false)]
    [InlineData("1.2.3.4.5", "1.2.3.4", false)]
    [InlineData("7", "7.0", false)]
    public void TryParse_reads_the_release_and_whether_it_is_a_pre_release(
        string text, string release, bool isPreRelease)
    {
        //Act
        bool parsed = PythonModuleFloors.TryParse(text, out int epoch, out Version version, out bool preRelease);

        //Assert
        parsed.Should().BeTrue();
        epoch.Should().Be(0);
        version.Should().Be(Version.Parse(release));
        preRelease.Should().Be(isPreRelease);
    }
}
