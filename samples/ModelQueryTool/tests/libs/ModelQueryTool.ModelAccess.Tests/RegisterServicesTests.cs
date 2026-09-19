using Microsoft.Extensions.DependencyInjection;
using ModelQueryTool.ModelAccess.Models;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// How an application wires the library up: one stager, shared, reached by either name, and built from
/// the options the application registered when it registered any.
/// </summary>
public class RegisterServicesTests
{
    /// <summary>The stager is one shared instance, whichever way it is asked for.</summary>
    [Fact]
    public void AddModelAccess_registers_one_shared_stager()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        var services = new ServiceCollection();
        services.AddSingleton(new ModelStagerOptions
        {
            Model = harness.CreateDescriptor(),
            RootDirectory = harness.RootDirectory
        });
        services.AddModelAccess();

        //Act
        using ServiceProvider provider = services.BuildServiceProvider();
        var stager = provider.GetRequiredService<ModelStager>();
        var asInterface = provider.GetRequiredService<IModelStager>();

        //Assert
        asInterface.Should().BeSameAs(stager);
        provider.GetRequiredService<ModelStager>().Should().BeSameAs(stager);
    }

    /// <summary>Options the application registered decide what is staged and where.</summary>
    [Fact]
    public void AddModelAccess_uses_the_options_the_application_registered()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        var services = new ServiceCollection();
        services.AddSingleton(new ModelStagerOptions
        {
            Model = harness.CreateDescriptor(),
            RootDirectory = harness.RootDirectory
        });
        services.AddModelAccess();

        //Act
        using ServiceProvider provider = services.BuildServiceProvider();
        IModelStager stager = provider.GetRequiredService<IModelStager>();

        //Assert
        stager.RootDirectory.Should().Be(harness.RootDirectory);
        stager.Model.Name.Should().Be(TinyModelHarness.ModelName);
    }

    /// <summary>
    /// With no options registered the defaults apply. Only the paths the stager works out are looked at
    /// here; nothing asks it to do anything, so no folder is read, created or removed.
    /// </summary>
    [Fact]
    public void AddModelAccess_falls_back_to_the_defaults_when_nothing_is_registered()
    {
        //Arrange
        var services = new ServiceCollection();
        services.AddModelAccess();

        //Act
        using ServiceProvider provider = services.BuildServiceProvider();
        IModelStager stager = provider.GetRequiredService<IModelStager>();

        //Assert
        stager.Model.Name.Should().Be(KnownModels.Qwen35.Name);
        stager.RootDirectory.Should().Be(ModelStagerOptions.ResolveDefaultRootDirectory());
    }
}
