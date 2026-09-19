using System.IO;
using ModelQueryTool.ModelAccess.Models;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// The defaults an application that sets nothing gets. Nothing here reads or writes the folder these
/// values name; only the path is worked out.
/// </summary>
public class ModelStagerOptionsTests
{
    /// <summary>The model nobody has to choose is the one the application talks to.</summary>
    [Fact]
    public void Model_is_the_one_the_application_talks_to() =>
        new ModelStagerOptions().Model.Should().BeSameAs(KnownModels.Qwen35);

    /// <summary>No folder is named until one is asked for.</summary>
    [Fact]
    public void RootDirectory_is_nothing_until_one_is_named() =>
        new ModelStagerOptions().RootDirectory.Should().BeNull();

    /// <summary>The store sits in a folder called after what it holds.</summary>
    [Fact]
    public void StoreFolderName_is_the_models_folder() =>
        ModelStagerOptions.StoreFolderName.Should().Be("models");

    /// <summary>
    /// The default folder is an absolute path that ends in the application's own name - including on a
    /// machine where local application data does not exist yet, which is the case this was written for.
    /// </summary>
    [Fact]
    public void ResolveDefaultRootDirectory_is_an_absolute_path_ending_in_the_application_name()
    {
        //Arrange
        string root = ModelStagerOptions.ResolveDefaultRootDirectory();

        //Assert
        root.Should().NotBeNullOrWhiteSpace();
        Path.IsPathRooted(root).Should().BeTrue();
        Path.GetFileName(root).Should().Be(ModelStagerOptions.ApplicationFolderName);
        ModelStagerOptions.ApplicationFolderName.Should().Be("ModelQueryTool");
    }
}
