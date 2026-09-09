using BitCode.Framework.Platform.Organization;
using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Infrastructure.Observability;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Web;
using BitCode.Framework.Shared.Infrastructure.Web.HealthChecks;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.OpenApi;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Organization.Api;

/// <summary>
/// Aplicación de referencia de Organization (Fase 6, módulo 2) -- mismo patrón que
/// <c>Sample.IdentityAdmin.Api/InfrastructureModule.cs</c>: agrupa los AddSharedX&lt;T&gt;() del
/// framework y de la librería BitCode.Platform.Organization.
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");
        var identityConnectionString = configuration.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Identity en la configuración.");

        // F1-12: registra la implementación productiva de ITenantProvider ANTES de AddSharedPersistence
        // -- Empresa/Sucursal/Area/Cargo implementan ITenantEntity.
        services.AddHttpContextTenantProvider();

        // OrganizationDbContext hereda directamente de MultiTenantDbContext (a diferencia de
        // IdentityAdministrationDbContext, que hereda de MultiTenantIdentityDbContext<,>) -- por eso
        // este módulo usa AddSharedPersistence<T> genérico tal cual, sin un método propio equivalente a
        // AddSharedIdentityAdministrationPersistence.
        services.AddSharedPersistence<OrganizationDbContext>(connectionString);

        // SampleIdentityDbContext es un DbContext de identidad PROPIO de este host de referencia (base
        // de datos separada, ConnectionStrings:Identity) -- Organization no es dueño de usuarios/roles,
        // ver el resumen de esa clase. AddSharedSecurity exige el DbContext ya registrado.
        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        // F2-08/F2-10: ABAC -- requerido por DesactivarEmpresaCommandHandler. Un consumidor real
        // configura aquí sus propias reglas de alcance (ScopeRules) sobre "organizacion.empresas" --
        // ver docs/guia-organization.md, sección "RBAC + ABAC en la desactivación de empresas". Este
        // proyecto de referencia no agrega ninguna regla de alcance (lista vacía = sin restricción
        // adicional más allá de RBAC), documentado explícitamente en la guía.
        services.AddSharedAbacAuthorization();

        // F2-15: auditoría (InMemoryAuditWriter, placeholder de referencia).
        services.AddSharedAuditing();

        services.AddSharedOrganization();

        // F1-22: ANTES de AddSharedApplication -- resuelve IIdempotencyKeyProvider desde el header
        // HTTP "Idempotency-Key" del request.
        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.Organization viven en su propio ensamblado -- deben
        // sumarse explícitamente al descubrimiento de MediatR/FluentValidation de este host.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(OrganizationDbContext).Assembly);
        services.AddSharedExceptionHandling();

        services.AddSharedApiVersioning();
        services.AddSharedOpenApiForApiVersion(1, options => options.AddJwtBearerSecurityScheme());

        services.AddSharedObservability(configuration);
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSharedHealthChecks();
        app.MapOpenApi();
    }
}
