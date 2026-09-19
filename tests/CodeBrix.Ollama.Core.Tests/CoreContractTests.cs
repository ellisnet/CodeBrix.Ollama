using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Covers the guard that turns a mixture of CodeBrix.Ollama package versions into one clear sentence: the
/// matching case passes silently, and the mismatched case says which library, which two revisions and what
/// to do about it.
/// </summary>
public sealed class CoreContractTests
{
    [Fact]
    public void LoadedRevision_is_the_revision_of_the_assembly_that_was_loaded() =>
        CoreContract.LoadedRevision.Should().Be(CoreContract.Revision);

    [Fact]
    public void Require_with_the_revision_it_was_built_against_does_nothing()
    {
        //Act
        Action act = () => CoreContract.Require(CoreContract.Revision, "CodeBrix.Ollama.ModelManager");

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Require_with_another_revision_names_the_library_and_both_revisions()
    {
        //Arrange
        int builtAgainst = CoreContract.Revision + 1;

        //Act
        Action act = () => CoreContract.Require(builtAgainst, "CodeBrix.Ollama.ModelRunner");

        //Assert
        string message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("CodeBrix.Ollama.ModelRunner");
        message.Should().Contain("contract revision " + builtAgainst);
        message.Should().Contain("contract revision " + CoreContract.LoadedRevision + " was loaded");
    }

    [Fact]
    public void Require_with_another_revision_says_to_install_one_version_of_every_package()
    {
        //Act
        Action act = () => CoreContract.Require(CoreContract.Revision - 1, "CodeBrix.Ollama.ModelManager");

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Install the SAME version of every CodeBrix.Ollama package.");
    }

    [Fact]
    public void Require_with_no_library_named_still_says_what_happened()
    {
        //Act
        Action act = () => CoreContract.Require(CoreContract.Revision + 7, null);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().StartWith("A CodeBrix.Ollama library was built against");
    }
}
