using BitCode.Framework.Platform.Identity;
using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Infrastructure.Observability;
using BitCode.Framework.Shared.Infrastructure.Security;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Web;
using BitCode.Framework.Shared.Infrastructure.Web.HealthChecks;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.OpenApi;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.IdentityAdmin.Api;

/// <summary>
/// Aplicación de referencia de Identity Administration (Fase 6, módulo 1) -- mismo patrón que
/// InfrastructureModule de Sample.Api (docs/convenciones.md): agrupa los AddSharedX&lt;T&gt;() del
/// framework y de la librería BitCode.Platform.Identity.
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");

        // Orden exigido por AddSharedIdentityAdministrationPersistence (ver sus XML docs): antes de
        // AddSharedSecurity, que a su vez requiere el DbContext ya registrado.
        services.AddSharedIdentityAdministrationPersistence(connectionString);
        services.AddSharedSecurity<ApplicationUser, ApplicationRole, IdentityAdministrationDbContext>(configuration);

        // F2-08/F2-10: ABAC + operaciones privilegiadas -- requerido por
        // AsignarRolAUsuarioCommandHandler (SelfRoleAssignmentAbacRule, registrada por
        // AddSharedIdentityAdministration más abajo, se suma a esta colección).
        services.AddSharedAbacAuthorization();

        // F2-15: auditoría (InMemoryAuditWriter, placeholder de referencia -- un consumidor real
        // registra su propia implementación persistente DESPUÉS de esta llamada).
        services.AddSharedAuditing();

        services.AddSharedIdentityAdministration(configuration);

        // F1-22: ANTES de AddSharedApplication -- resuelve IIdempotencyKeyProvider desde el header
        // HTTP "Idempotency-Key" del request (mismo patrón que Sample.Api/InfrastructureModule.cs);
        // sin este registro, todo IIdempotentCommand (CrearUsuarioCommand, AsignarRolAUsuarioCommand,
        // etc.) se rechazaría siempre con "Idempotency.KeyRequired".
        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.Identity viven en su propio ensamblado -- deben
        // sumarse explícitamente al descubrimiento de MediatR/FluentValidation de este host.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(IdentityAdministrationDbContext).Assembly);
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
