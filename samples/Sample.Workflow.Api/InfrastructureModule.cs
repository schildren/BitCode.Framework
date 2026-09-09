using BitCode.Framework.Platform.Workflow;
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

namespace Sample.Workflow.Api;

/// <summary>
/// Aplicación de referencia de Workflow (Fase 6, módulo 6) -- mismo patrón que
/// <c>Sample.Catalogs.Api/InfrastructureModule.cs</c> (Fase 6, módulo 3): agrupa los
/// <c>AddSharedX&lt;T&gt;()</c> del framework y de la librería <c>BitCode.Platform.Workflow</c>. No
/// registra <c>AddSharedBackgroundJobs</c>/<c>WorkflowEscalamientoJob</c> -- el escalamiento por SLA se
/// verifica en <c>Sample.Workflow.Api.Tests</c> invocando el job directamente contra el mismo
/// <see cref="WorkflowDbContext"/> (ver <c>docs/guia-workflow.md</c>, sección "Timeout y SLA", para cómo
/// lo agregaría un consumidor productivo con Quartz HA real).
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");
        var identityConnectionString = configuration.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Identity en la configuración.");

        // F1-12: registra la implementación productiva de ITenantProvider ANTES de AddSharedPersistence.
        services.AddHttpContextTenantProvider();

        services.AddSharedPersistence<WorkflowDbContext>(connectionString);

        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        services.AddSharedAbacAuthorization();

        // F2-15: auditoría (InMemoryAuditWriter, placeholder de referencia).
        services.AddSharedAuditing();

        services.AddSharedWorkflow();

        // F1-22: ANTES de AddSharedApplication -- resuelve IIdempotencyKeyProvider desde el header
        // HTTP "Idempotency-Key" del request.
        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.Workflow viven en su propio ensamblado -- deben
        // sumarse explícitamente al descubrimiento de MediatR/FluentValidation de este host.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(WorkflowDbContext).Assembly);
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
