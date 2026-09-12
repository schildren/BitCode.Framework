using MyApp.Modules.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace MyApp.Modules;

/// <summary>
/// Envuelve el cableado de este módulo (mismo espíritu que
/// <c>src/Platform/BitCode.Platform.Dashboard/DashboardServiceCollectionExtensions.cs</c>). Un
/// consumidor real llama <c>services.AddSharedPersistence&lt;ModuleNameDbContext&gt;(connectionString)</c>
/// directamente en su propio <c>InfrastructureModule</c> ANTES de llamar a este método, y agrega el
/// ensamblado de este módulo a <c>services.AddSharedApplication(...)</c> para que sus handlers/validators
/// (que viven en este ensamblado, no en el del host) se registren -- ver README.md.
/// </summary>
public static class ModuleNameServiceCollectionExtensions
{
    public static IServiceCollection AddSharedModuleName(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<ModuleNameDbContextHealthCheck>("sql-server-modulename", tags: ["ready"]);

        return services;
    }
}
