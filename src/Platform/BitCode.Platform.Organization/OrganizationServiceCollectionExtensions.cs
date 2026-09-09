using BitCode.Framework.Platform.Organization.Actors;
using BitCode.Framework.Platform.Organization.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Organization;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Organization (Fase 6, módulo 2) -- mismo espíritu que
/// <c>IdentityAdministrationServiceCollectionExtensions</c> (Fase 6, módulo 1). A diferencia de
/// Identity Administration, <see cref="OrganizationDbContext"/> hereda directamente de
/// <c>MultiTenantDbContext</c> (no de <c>MultiTenantIdentityDbContext&lt;,&gt;</c>), así que la
/// persistencia se registra con <c>AddSharedPersistence&lt;OrganizationDbContext&gt;</c> (Shared.
/// Infrastructure.Persistence, F1-12/F1-19) tal cual, sin un método propio equivalente a
/// <c>AddSharedIdentityAdministrationPersistence</c> -- un consumidor real llama
/// <c>services.AddSharedPersistence&lt;OrganizationDbContext&gt;(connectionString)</c> directamente en su
/// propio <c>InfrastructureModule</c> (ver <c>Sample.Organization.Api</c>), que ya deja resueltos
/// <c>IRepository&lt;,&gt;</c>/<c>IReadRepository&lt;,&gt;</c>/<c>IUnitOfWork</c>/
/// <c>IIdempotencyStore</c>/<c>IInboxStore</c> para las cuatro entidades de este módulo (genéricos, sin
/// registro adicional por tipo) y el health check "sql-server" de lectura genérico.
/// </summary>
public static class OrganizationServiceCollectionExtensions
{
    /// <summary>
    /// Registra los servicios propios de aplicación del módulo (resolución de actor para
    /// auditoría/ABAC y el health check propio con nombre distintivo). No registra
    /// <c>AddSharedSecurity</c>/<c>AddSharedAbacAuthorization</c>/<c>AddSharedAuditing</c> por su cuenta
    /// -- son responsabilidad explícita del host consumidor (mismo criterio que Identity
    /// Administration, ver <c>docs/guia-organization.md</c>, sección "Orden de registro"). Debe
    /// llamarse DESPUÉS de <c>AddSharedPersistence&lt;OrganizationDbContext&gt;</c> (necesita
    /// <see cref="OrganizationDbContext"/> ya registrado para el health check propio).
    /// </summary>
    public static IServiceCollection AddSharedOrganization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IOrganizationActorContext, HttpContextOrganizationActorContext>();

        services.AddHealthChecks()
            .AddCheck<OrganizationDbContextHealthCheck>("sql-server-organization", tags: ["ready"]);

        return services;
    }
}
