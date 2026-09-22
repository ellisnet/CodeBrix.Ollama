using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the scripts that ship inside the package: that they are read out of the assembly rather than
/// off disk, that their line endings are the same however the repository was checked out, and that
/// asking for one that does not exist fails at once.
/// </summary>
public sealed class PythonScriptsTests
{
    [Fact]
    public void Read_returns_the_version_probe()
    {
        //Act
        string text = PythonScripts.Read(PythonScripts.ProbeVersion);

        //Assert
        text.Should().NotBeEmpty();
        text.Should().Contain("import sys");
        text.Should().Contain("result");
        text.Should().Contain("sys.prefix");
    }

    [Fact]
    public void Names_lists_every_script_that_ships_in_this_package()
        => PythonScripts.Names().Should().Equal(
            PythonScripts.ExportGenAi,
            PythonScripts.ExportMuseCoco,
            PythonScripts.ExportOptimum,
            PythonScripts.ProbeVersion,
            PythonScripts.ReduceDynamic,
            PythonScripts.ReducePreprocess,
            PythonScripts.ReduceWeightOnly);

    [Fact]
    public void Read_returns_a_reduction_script_that_leaves_its_answer_behind()
    {
        //Act
        string text = PythonScripts.Read(PythonScripts.ReduceWeightOnly);

        //Assert
        text.Should().Contain("MatMulNBitsQuantizer");
        text.Should().Contain("DefaultWeightOnlyQuantConfig");
        text.Should().Contain("result");
    }

    [Fact]
    public void Read_hands_back_the_same_text_twice()
        => PythonScripts.Read(PythonScripts.ProbeVersion)
            .Should().Be(PythonScripts.Read(PythonScripts.ProbeVersion));

    [Fact]
    public void Read_leaves_no_carriage_returns_behind()
        => PythonScripts.Read(PythonScripts.ProbeVersion).Should().NotContain("\r");

    [Fact]
    public void Read_with_a_name_that_ships_in_no_package_throws()
    {
        //Arrange
        Action act = () => PythonScripts.Read("not_a_script.py");

        //Act and assert
        act.Should().Throw<ModelManagerException>().Which.Message.Should().Contain("not_a_script.py");
    }

    [Fact]
    public void Read_with_a_blank_name_throws()
    {
        //Arrange
        Action act = () => PythonScripts.Read("   ");

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\n\rb", "a\n\nb")]
    [InlineData("", "")]
    public void NormalizeLineEndings_turns_every_ending_into_a_line_feed(string text, string expected)
        => PythonScripts.NormalizeLineEndings(text).Should().Be(expected);

    [Fact]
    public void NormalizeLineEndings_with_null_returns_null()
        => PythonScripts.NormalizeLineEndings(null).Should().BeNull();

    [Fact]
    public void ResultVariable_is_the_name_every_script_leaves_its_answer_in()
        => PythonScripts.ResultVariable.Should().Be("result");
}
