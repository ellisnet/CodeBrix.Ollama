using System.IO;
using System.Runtime.InteropServices;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the message a consumer sees when a module is installed but older than the lowest version this
/// library runs: which module, which versions, which interpreter, and the command that upgrades it THERE.
/// </summary>
public sealed class PythonModuleTooOldExceptionTests
{
    private const string Library = "/usr/lib/x86_64-linux-gnu/libpython3.13.so";
    private const string Environment = "/home/someone/venvs/work";

    [Fact]
    public void the_message_names_the_module_both_versions_the_interpreter_and_the_command()
    {
        //Act
        var thrown = new PythonModuleTooOldException(
            "exporting to ONNX", "torch", "2.5.1+cpu", "2.6", Library, Environment);

        //Assert
        thrown.Message.Should().StartWith("The Python module 'torch' in the CPython at " + Library);
        thrown.Message.Should().Contain("(virtual environment " + Environment + ")");
        thrown.Message.Should().Contain("is version 2.5.1+cpu, and at least 2.6 is required");
        thrown.Message.Should().Contain(PythonModuleTooOldException.UpgradeCommand("torch", "2.6", Environment));
        thrown.Message.Should().EndWith("It is needed for exporting to ONNX.");
        thrown.Feature.Should().Be("exporting to ONNX");
        thrown.ModuleName.Should().Be("torch");
        thrown.InstalledVersion.Should().Be("2.5.1+cpu");
        thrown.MinimumVersion.Should().Be("2.6");
        thrown.Should().BeAssignableTo<ModelManagerException>();
    }

    [Fact]
    public void the_message_for_an_unreadable_version_says_so()
    {
        //Act
        var thrown = new PythonModuleTooOldException("exporting to ONNX", "onnx", null, "1.16", Library, null);

        //Assert
        thrown.Message.Should().Contain("is of a version that could not be read, and at least 1.16 is required");
        thrown.Message.Should().Contain("(no virtual environment)");
        thrown.Message.Should().Contain("Upgrade it there with: pip install --upgrade \"onnx>=1.16\"");
    }

    [Fact]
    public void UpgradeCommand_uses_the_environments_own_pip()
    {
        //Act
        string command = PythonModuleTooOldException.UpgradeCommand("transformers", "5.10", Environment);

        //Assert
        string pip = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment, "Scripts", "pip.exe")
            : Environment + "/bin/pip";
        command.Should().Be(pip + " install --upgrade \"transformers>=5.10\"");
    }
}
