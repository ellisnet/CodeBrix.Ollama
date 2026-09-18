using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the message a consumer sees when one of the scripts that ship in this package fails for a
/// reason other than a missing module: which script, what Python said, and what was running it.
/// </summary>
public sealed class PythonScriptExceptionTests
{
    [Fact]
    public void the_message_names_the_script_what_python_said_and_the_feature()
    {
        //Act
        var thrown = new PythonScriptException(
            "exporting to ONNX", "export_onnx.py", "ValueError: unsupported architecture", null);

        //Assert
        thrown.Message.Should().StartWith("The Python script 'export_onnx.py' failed: ");
        thrown.Message.Should().Contain("ValueError: unsupported architecture");
        thrown.Message.Should().EndWith("It was running for exporting to ONNX.");
    }

    [Fact]
    public void every_value_it_is_given_is_the_value_it_reports()
    {
        //Arrange
        var cause = new InvalidOperationException("the cause");

        //Act
        var thrown = new PythonScriptException("reducing an ONNX model", "reduce.py", "MemoryError", cause);

        //Assert
        thrown.Feature.Should().Be("reducing an ONNX model");
        thrown.ScriptName.Should().Be("reduce.py");
        thrown.PythonMessage.Should().Be("MemoryError");
        thrown.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void it_is_a_ModelManagerException()
        => new PythonScriptException("anything", "any.py", "any message", null)
            .Should().BeAssignableTo<ModelManagerException>();

    [Fact]
    public void a_missing_script_name_and_message_are_said_rather_than_left_blank()
        => new PythonScriptException(null, null, null, null)
            .Message.Should().Be("The Python script '(unnamed script)' failed: (no message).");
}
