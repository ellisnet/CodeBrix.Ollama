using Microsoft.Extensions.DependencyInjection;

namespace ModelQueryTool.ModelAccess;

/// <summary>Registers everything this library offers.</summary>
public static class RegisterServices
{
    /// <summary>
    /// Adds the stager as one shared instance, reached either by its own type or through
    /// <see cref="IModelStager"/>. A <see cref="ModelStagerOptions"/> the application registered is
    /// used; with none, the model and folder the application uses apply.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddModelAccess(this IServiceCollection services)
    {
        services.AddSingleton<ModelStager>(provider =>
            new ModelStager(provider.GetService<ModelStagerOptions>()));
        services.AddSingleton<IModelStager>(provider => provider.GetRequiredService<ModelStager>());
        return services;
    }
}
