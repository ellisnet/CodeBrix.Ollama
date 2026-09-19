using Microsoft.Extensions.DependencyInjection;

namespace ModelQueryTool.ModelRunning;

/// <summary>Registers everything this library offers.</summary>
public static class RegisterServices
{
    /// <summary>
    /// Adds the model host as a singleton, reachable both as <see cref="ModelHost"/> and as
    /// <see cref="IModelHost"/> - one instance, because it owns a loaded model. A
    /// <see cref="ModelHostOptions"/> registered before this call is used; otherwise the defaults are.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModelRunning(this IServiceCollection services)
    {
        services.AddSingleton<ModelHost>(provider => new ModelHost(provider.GetService<ModelHostOptions>()));
        services.AddSingleton<IModelHost>(provider => provider.GetRequiredService<ModelHost>());

        return services;
    }
}
