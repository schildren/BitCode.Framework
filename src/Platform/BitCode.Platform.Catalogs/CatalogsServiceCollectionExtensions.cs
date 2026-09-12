using BitCode.Framework.Platform.Catalogs.Actors;
using BitCode.Framework.Platform.Catalogs.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Catalogs;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Catalogs and Parameters (Fase 6, módulo 3) -- mismo
/// espíritu que <c>OrganizationServiceCollectionExtensions</c> (Fase 6, módulo 2). Un consumidor real
/// llama <c>services.AddSharedPersistence&lt;CatalogsDbContext&gt;(connectionString)</c> directamente en
/// su propio <c>InfrastructureModule</c> (ver <c>Sample.Catalogs.Api</c>), que ya deja resueltos
/// <c>IRepository&lt;,&gt;</c>/<c>IReadRepository&lt;,&gt;</c>/<c>IUnitOfWork</c>/
/// <c>IIdempotencyStore</c>/<c>IInboxStore</c> para las cinco entidades de este módulo (genéricos, sin
/// registro adicional por tipo) y el health check "sql-server" de lectura genérico.
/// </summary>
public static class CatalogsServiceCollectionExtensions
{
    /// <summary>
    /// Registra los servicios propios de aplicación del módulo (resolución de actor para
    /// auditoría/ABAC y el health check propio con nombre distintivo). No registra
    /// <c>AddSharedSecurity</c>/<c>AddSharedAbacAuthorization</c>/<c>AddSharedAuditing</c> por su cuenta
    /// -- son responsabilidad explícita del host consumidor (mismo criterio que Organization, ver
    /// <c>docs/guia-catalogs.md</c>, sección "Orden de registro"). Debe llamarse DESPUÉS de
    /// <c>AddSharedPersistence&lt;CatalogsDbContext&gt;</c> (necesita <see cref="CatalogsDbContext"/> ya
    /// registrado para el health check propio).
    /// </summary>
    public static IServiceCollection AddSharedCatalogs(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICatalogsActorContext, HttpContextCatalogsActorContext>();

        services.AddHealthChecks()
            .AddCheck<CatalogsDbContextHealthCheck>("sql-server-catalogs", tags: ["ready"]);

        return services;
    }
}
