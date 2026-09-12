using BitCode.Framework.Platform.Reporting;
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

namespace Sample.Reporting.Api;

/// <summary>
/// Aplicación de referencia de Reporting (Fase 6, módulo 11) -- mismo patrón que
/// <c>Sample.TaskInbox.Api/InfrastructureModule.cs</c> (Fase 6, módulo 7). Deliberadamente NO registra
/// <c>AddSharedPersistence&lt;WorkflowDbContext&gt;</c> en el mismo host: combinar dos
/// <c>AddSharedPersistence&lt;T&gt;</c> de dos bounded contexts de Fase 6 distintos en un único proceso
/// es hoy inseguro (mismo hallazgo ya documentado por Task Inbox, ver <c>docs/guia-reporting.md</c>,
/// sección "Límites conocidos"). Este host es, por diseño, Reporting en aislamiento: los dos eventos de
/// integración de Workflow que consume se simulan en las pruebas de integración
/// (<c>Sample.Reporting.Api.Tests</c>) construyendo directamente los <c>record</c> públicos de
/// <c>BitCode.Platform.Workflow</c> -- exactamente como llegarían de un despliegue real de Workflow vía
/// Kafka, sin que este proceso dependa de <c>WorkflowDbContext</c> en ningún momento.
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");
        var identityConnectionString = configuration.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Identity en la configuración.");

        services.AddHttpContextTenantProvider();

        services.AddSharedPersistence<ReportingDbContext>(connectionString);

        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        services.AddSharedAbacAuthorization();
        services.AddSharedAuditing();

        services.AddSharedReporting();

        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.Reporting viven en su propio ensamblado.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(ReportingDbContext).Assembly);
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
