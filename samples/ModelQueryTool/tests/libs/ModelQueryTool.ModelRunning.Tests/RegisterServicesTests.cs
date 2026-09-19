using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelRunning.Tests;

/// <summary>Putting the host into a service collection.</summary>
public class RegisterServicesTests
{
    /// <summary>The registration hands back the collection it was given.</summary>
    [Fact]
    public void AddModelRunning_returns_the_same_collection()
    {
        //Arrange
        ServiceCollection services = new ServiceCollection();

        //Assert
        services.AddModelRunning().Should().BeSameAs(services);
    }

    /// <summary>Both ways of asking for the host reach one instance, because it owns a loaded model.</summary>
    [Fact]
    public async Task the_host_is_one_shared_instance()
    {
        //Arrange
        ServiceCollection services = new ServiceCollection();
        services.AddModelRunning();

        //Act
        await using ServiceProvider provider = services.BuildServiceProvider();
        IModelHost asContract = provider.GetRequiredService<IModelHost>();
        ModelHost asImplementation = provider.GetRequiredService<ModelHost>();

        //Assert
        asContract.Should().BeSameAs(asImplementation);
        provider.GetRequiredService<IModelHost>().Should().BeSameAs(asContract);
    }

    /// <summary>With no options registered the host takes the defaults.</summary>
    [Fact]
    public async Task the_host_takes_the_default_options_when_none_are_registered()
    {
        //Arrange
        ServiceCollection services = new ServiceCollection();
        services.AddModelRunning();

        //Act
        await using ServiceProvider provider = services.BuildServiceProvider();
        IModelHost host = provider.GetRequiredService<IModelHost>();

        //Assert
        host.ContextSize.Should().Be(8192u);
        host.Think.Should().Be(true);
        host.SystemPrompt.Should().BeNull();
    }

    /// <summary>Options registered before the host are the ones it uses.</summary>
    [Fact]
    public async Task the_host_uses_options_that_were_registered()
    {
        //Arrange
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(new ModelHostOptions
        {
            ContextSize = 2048,
            Think = false,
            SystemPrompt = "be brief",
        });
        services.AddModelRunning();

        //Act
        await using ServiceProvider provider = services.BuildServiceProvider();
        IModelHost host = provider.GetRequiredService<IModelHost>();

        //Assert
        host.ContextSize.Should().Be(2048u);
        host.Think.Should().Be(false);
        host.SystemPrompt.Should().Be("be brief");
    }
}
