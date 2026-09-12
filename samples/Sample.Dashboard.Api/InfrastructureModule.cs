using BitCode.Framework.Platform.Dashboard;
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

namespace Sample.Dashboard.Api;

/// <summary>
/// Aplicación de referencia de Dashboard (Fase 6, módulo 12 -- el ÚLTIMO módulo de la fase) -- mismo
/// patrón que <c>Sample.Reporting.Api/InfrastructureModule.cs</c> (Fase 6, módulo 11).
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");
        var identityConnectionString = configuration.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Identity en la configuración.");
        var reportingBaseUrl = configuration["Dashboard:ReportingBaseUrl"]
            ?? throw new InvalidOperationException("Falta Dashboard:ReportingBaseUrl en la configuración.");

        services.AddHttpContextTenantProvider();

        services.AddSharedPersistence<DashboardDbContext>(connectionString);

        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        services.AddSharedAbacAuthorization();
        services.AddSharedAuditing();

        services.AddSharedDashboard(
            reportingBaseUrl,
            configureHttpResilience: options => configuration.GetSection("Dashboard:HttpResilience").Bind(options));

        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.Dashboard viven en su propio ensamblado.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(DashboardDbContext).Assembly);
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
