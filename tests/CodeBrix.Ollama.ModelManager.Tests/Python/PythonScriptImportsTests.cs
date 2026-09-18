using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the allow-list every script that ships in this package is read against: what it lets through,
/// what it refuses, and that the scripts actually in the package pass it.
/// </summary>
public sealed class PythonScriptImportsTests
{
    [Fact]
    public void Permitted_names_the_modules_this_packages_scripts_may_import()
        => PythonScriptImports.Permitted.Should().Contain("sys");

    [Fact]
    public void every_script_that_ships_in_this_package_is_read_against_the_allow_list()
    {
        //Arrange
        //Read out of the assembly, not from a list in this file: a script added to the package without
        //its imports being allowed for has to fail HERE, and a list somebody forgets to add to would let
        //it through.
        IReadOnlyList<string> shipped = PythonScripts.Names();

        //Act and assert
        shipped.Should().Contain(PythonScripts.ProbeVersion);
        foreach (string scriptName in shipped)
        {
            PythonScriptImports.TryFindForbiddenImport(PythonScripts.Read(scriptName), out string offendingLine)
                .Should().BeFalse(scriptName + " imports " + offendingLine);
        }
    }

    [Theory]
    [InlineData("sys", true)]
    [InlineData("sys.path", true)]
    [InlineData("os", true)]
    [InlineData("os.path", true)]
    [InlineData("onnxruntime_genai", true)]
    [InlineData("shutil", false)]
    [InlineData("subprocess", false)]
    [InlineData("*", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPermitted_answers_for_one_module_name(string moduleName, bool expected)
        => PythonScriptImports.IsPermitted(moduleName).Should().Be(expected);

    [Fact]
    public void TryFindForbiddenImport_accepts_the_version_probe_that_ships_in_this_package()
    {
        //Act
        bool refused = PythonScriptImports.TryFindForbiddenImport(
            PythonScripts.Read(PythonScripts.ProbeVersion), out string offendingLine);

        //Assert
        refused.Should().BeFalse();
        offendingLine.Should().BeNull();
    }

    [Theory]
    [InlineData("import sys")]
    [InlineData("import sys\nresult = {}")]
    [InlineData("from sys import version_info")]
    [InlineData("import sys as system")]
    [InlineData("# import shutil\nimport sys")]
    [InlineData("import os\nimport sys")]
    [InlineData("from onnxruntime_genai.models import builder")]
    [InlineData("result = 1")]
    [InlineData("")]
    public void TryFindForbiddenImport_lets_an_allowed_script_through(string scriptText)
        => PythonScriptImports.TryFindForbiddenImport(scriptText, out _).Should().BeFalse();

    [Theory]
    [InlineData("import shutil")]
    [InlineData("import sys, shutil")]
    [InlineData("from shutil import copyfile")]
    [InlineData("from sys import *")]
    [InlineData("import subprocess as sp")]
    [InlineData("import sys\n    import shutil")]
    public void TryFindForbiddenImport_refuses_what_the_allow_list_does_not_name(string scriptText)
    {
        //Act
        bool refused = PythonScriptImports.TryFindForbiddenImport(scriptText, out string offendingLine);

        //Assert
        refused.Should().BeTrue();
        offendingLine.Should().NotBeEmpty();
    }

    [Fact]
    public void Check_throws_and_names_the_script_and_the_line()
    {
        //Arrange
        Action act = () => PythonScriptImports.Check("made_up.py", "import shutil");

        //Act and assert
        ModelManagerException thrown = act.Should().Throw<ModelManagerException>().Which;
        thrown.Message.Should().Contain("made_up.py");
        thrown.Message.Should().Contain("import shutil");
    }

    [Fact]
    public void Check_says_nothing_about_a_script_that_only_imports_what_it_may()
    {
        //Arrange
        Action act = () => PythonScriptImports.Check("fine.py", "import sys\nresult = {'ok': True}");

        //Act and assert
        act.Should().NotThrow();
    }
}
