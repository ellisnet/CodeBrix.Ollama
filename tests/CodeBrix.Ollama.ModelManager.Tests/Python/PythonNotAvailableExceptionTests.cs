using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the message a consumer with no usable CPython sees: what is wrong, the range that is supported,
/// and the feature that needed it.
/// </summary>
public sealed class PythonNotAvailableExceptionTests
{
    private static readonly Version Minimum = new Version(3, 10);
    private static readonly Version Maximum = new Version(3, 15, int.MaxValue, int.MaxValue);

    [Fact]
    public void the_message_for_nothing_found_names_every_way_of_naming_a_library()
    {
        //Act
        var thrown = new PythonNotAvailableException("exporting to ONNX", Minimum, Maximum);

        //Assert
        thrown.Message.Should().Contain("No CPython shared library was found.");
        thrown.Message.Should().Contain("PythonOptions.LibraryPath");
        thrown.Message.Should().Contain("PYTHONNET_PYDLL");
        thrown.Message.Should().Contain("libpython3.XX.so / python3XX.dll / libpython3.XX.dylib");
        thrown.Message.Should().Contain("the supported range is 3.10 to 3.15.");
        thrown.Message.Should().EndWith("It is needed for exporting to ONNX.");
    }

    [Fact]
    public void the_message_for_a_library_that_was_found_says_that_instead()
    {
        //Act
        var thrown = new PythonNotAvailableException(
            "reducing an ONNX model",
            "CPython 3.9 at /usr/lib/libpython3.9.so is outside the supported range.",
            Minimum,
            Maximum);

        //Assert
        thrown.Message.Should().StartWith("CPython 3.9 at /usr/lib/libpython3.9.so");
        thrown.Message.Should().NotContain("No CPython shared library was found");
        thrown.Message.Should().Contain("The supported range is 3.10 to 3.15.");
        thrown.Message.Should().EndWith("It is needed for reducing an ONNX model.");
    }

    [Fact]
    public void Feature_is_what_it_was_given()
        => new PythonNotAvailableException("exporting to ONNX", Minimum, Maximum)
            .Feature.Should().Be("exporting to ONNX");

    [Fact]
    public void it_is_a_ModelManagerException()
        => new PythonNotAvailableException("anything", Minimum, Maximum)
            .Should().BeAssignableTo<ModelManagerException>();

    [Fact]
    public void an_unknown_range_is_said_rather_than_left_blank()
        => new PythonNotAvailableException("anything", null, null)
            .Message.Should().Contain("the supported range is (unknown) to (unknown).");

    [Fact]
    public void without_a_feature_the_message_simply_stops()
        => new PythonNotAvailableException(null, Minimum, Maximum)
            .Message.Should().EndWith("the supported range is 3.10 to 3.15.");
}
