using System.IO;
using System.Runtime.InteropServices;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the message a consumer sees when the interpreter is fine but a module is missing: which module,
/// which interpreter, which environment, and the command that installs it THERE rather than somewhere
/// else on the machine.
/// </summary>
public sealed class PythonModuleNotInstalledExceptionTests
{
    private const string Library = "/usr/lib/x86_64-linux-gnu/libpython3.13.so";
    private const string Environment = "/home/someone/venvs/work";

    [Fact]
    public void the_message_names_the_module_the_interpreter_the_environment_and_the_command()
    {
        //Act
        var thrown = new PythonModuleNotInstalledException(
            "exporting to ONNX", "onnxruntime", Library, Environment);

        //Assert
        thrown.Message.Should().StartWith("The Python module 'onnxruntime' is not installed in the CPython at ");
        thrown.Message.Should().Contain(Library);
        thrown.Message.Should().Contain("(virtual environment " + Environment + ")");
        thrown.Message.Should().Contain("Install it there with: ");
        thrown.Message.Should().Contain(PythonModuleNotInstalledException.InstallCommand("onnxruntime", Environment));
        thrown.Message.Should().EndWith("It is needed for exporting to ONNX.");
    }

    [Fact]
    public void the_message_without_an_environment_says_there_is_none_and_asks_for_a_plain_install()
    {
        //Act
        var thrown = new PythonModuleNotInstalledException("exporting to ONNX", "onnx", Library, null);

        //Assert
        thrown.Message.Should().Contain("(no virtual environment)");
        thrown.Message.Should().Contain("Install it there with: pip install onnx");
    }

    [Fact]
    public void InstallCommand_uses_the_environments_own_pip()
    {
        //Act
        string command = PythonModuleNotInstalledException.InstallCommand("onnxruntime", Environment);

        //Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command.Should().Be(Path.Combine(Environment, "Scripts", "pip.exe") + " install onnxruntime");
        }
        else
        {
            command.Should().Be(Environment + "/bin/pip install onnxruntime");
        }
    }

    [Fact]
    public void InstallCommand_without_an_environment_is_a_plain_install()
        => PythonModuleNotInstalledException.InstallCommand("torch", null).Should().Be("pip install torch");

    [Fact]
    public void ModuleName_and_Feature_are_what_they_were_given()
    {
        //Act
        var thrown = new PythonModuleNotInstalledException("exporting to ONNX", "optimum", Library, Environment);

        //Assert
        thrown.ModuleName.Should().Be("optimum");
        thrown.Feature.Should().Be("exporting to ONNX");
    }

    [Fact]
    public void it_is_a_ModelManagerException()
        => new PythonModuleNotInstalledException("anything", "onnx", Library, null)
            .Should().BeAssignableTo<ModelManagerException>();

    [Fact]
    public void an_unknown_interpreter_path_is_said_rather_than_left_blank()
        => new PythonModuleNotInstalledException("anything", "onnx", null, null)
            .Message.Should().Contain("the CPython at (unknown path)");
}
