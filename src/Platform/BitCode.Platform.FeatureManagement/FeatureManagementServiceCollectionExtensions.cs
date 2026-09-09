using BitCode.Framework.Platform.FeatureManagement.Actors;
using BitCode.Framework.Platform.FeatureManagement.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.FeatureManagement;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Feature Management (Fase 6, módulo 4) -- mismo espíritu
/// que <c>CatalogsServiceCollectionExtensions</c>/<c>OrganizationServiceCollectionExtensions</c>. Un
/// consumidor real llama <c>services.AddSharedPersistence&lt;FeatureManagementDbContext&gt;(connectionString)</c>
/// directamente en su propio <c>InfrastructureModule</c> (ver <c>Sample.FeatureManagement.Api</c>), que
/// ya deja resueltos <c>IRepository&lt;,&gt;</c>/<c>IReadRepository&lt;,&gt;</c>/<c>IUnitOfWork</c>/
/// <c>IIdempotencyStore</c>/<c>IInboxStore</c> para las tres entidades de este módulo (genéricos, sin
/// registro adicional por tipo) y el health check "sql-server" de lectura genérico.
/// </summary>
public static class FeatureManagementServiceCollectionExtensions
{
    /// <summary>
    /// Registra los servicios propios de aplicación del módulo (resolución de actor para
    /// auditoría/ABAC y el health check propio con nombre distintivo). No registra
    /// <c>AddSharedSecurity</c>/<c>AddSharedAbacAuthorization</c>/<c>AddSharedAuditing</c> por su cuenta
    /// -- son responsabilidad explícita del host consumidor (mismo criterio que Catalogs/Organization,
    /// ver <c>docs/guia-feature-management.md</c>, sección "Orden de registro"). Debe llamarse DESPUÉS de
    /// <c>AddSharedPersistence&lt;FeatureManagementDbContext&gt;</c> (necesita
    /// <see cref="FeatureManagementDbContext"/> ya registrado para el health check propio).
    /// </summary>
    public static IServiceCollection AddSharedFeatureManagement(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IFeatureManagementActorContext, HttpContextFeatureManagementActorContext>();

        services.AddHealthChecks()
            .AddCheck<FeatureManagementDbContextHealthCheck>("sql-server-featuremanagement", tags: ["ready"]);

        return services;
    }
}
