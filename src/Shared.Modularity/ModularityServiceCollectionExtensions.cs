using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Modularity;

public static class ModularityServiceCollectionExtensions
{
    /// <summary>
    /// Descubre por reflexión toda clase concreta que implemente IFrameworkModule en los
    /// assemblies indicados, la instancia (requiere constructor sin parámetros) y llama a su
    /// ConfigureServices. El orden de ejecución respeta las dependencias declaradas con
    /// [DependsOn] (ver ModuleDependencyResolver).
    /// </summary>
    public static IServiceCollection AddModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] assemblies)
    {
        var moduleTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(IFrameworkModule).IsAssignableFrom(type))
            .ToArray();

        var orderedTypes = ModuleDependencyResolver.OrderByDependencies(moduleTypes);

        foreach (var moduleType in orderedTypes)
        {
            var constructor = moduleType.GetConstructor(Type.EmptyTypes);
            if (constructor is null)
            {
                throw new InvalidOperationException(
                    $"El módulo '{moduleType.FullName}' debe tener un constructor público sin parámetros.");
            }

            var module = (IFrameworkModule)constructor.Invoke(null);
            module.ConfigureServices(services, configuration);

            // Se registra la instancia (no solo se invoca ConfigureServices) para que código
            // posterior — p.ej. UseModules() en Shared.Infrastructure.Web, que configura el
            // pipeline HTTP de los módulos que también implementan IWebFrameworkModule — pueda
            // recuperar los módulos ya resueltos, respetando el mismo orden por DependsOn.
            services.AddSingleton(moduleType, module);
            services.AddSingleton<IFrameworkModule>(module);
        }

        return services;
    }
}
