using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the shape of <see cref="PythonOptions"/>: everything unset by default, the two environment
/// variable names it publishes, and the section a store's options always carry.
/// </summary>
public sealed class PythonOptionsTests
{
    [Fact]
    public void VirtualEnvironment_defaults_to_null()
        => new PythonOptions().VirtualEnvironment.Should().BeNull();

    [Fact]
    public void LibraryPath_defaults_to_null()
        => new PythonOptions().LibraryPath.Should().BeNull();

    [Fact]
    public void VirtualEnvironmentVariable_names_this_librarys_own_variable()
        => PythonOptions.VirtualEnvironmentVariable.Should().Be("CODEBRIX_OLLAMA_PYTHON_VENV");

    [Fact]
    public void LibraryPathVariable_names_the_embedding_layers_variable()
        => PythonOptions.LibraryPathVariable.Should().Be("PYTHONNET_PYDLL");

    [Fact]
    public void both_properties_round_trip()
    {
        //Arrange
        var options = new PythonOptions();

        //Act
        options.VirtualEnvironment = "/home/someone/venvs/work";
        options.LibraryPath = "/usr/lib/libpython3.13.so";

        //Assert
        options.VirtualEnvironment.Should().Be("/home/someone/venvs/work");
        options.LibraryPath.Should().Be("/usr/lib/libpython3.13.so");
    }

    [Fact]
    public void ModelStoreOptions_hands_out_a_Python_section_by_default()
    {
        //Arrange
        var options = new ModelStoreOptions();

        //Act and assert
        options.Python.Should().NotBeNull();
        options.Python.VirtualEnvironment.Should().BeNull();
        options.Python.LibraryPath.Should().BeNull();
    }

    [Fact]
    public void ModelStoreOptions_Python_can_be_replaced()
    {
        //Arrange
        var replacement = new PythonOptions { LibraryPath = "/opt/python/libpython3.12.so" };
        var options = new ModelStoreOptions();

        //Act
        options.Python = replacement;

        //Assert
        options.Python.Should().BeSameAs(replacement);
    }
}
