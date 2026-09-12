using BitCode.Framework.Platform.IntegrationHub;
using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Infrastructure.Observability;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using BitCode.Framework.Shared.Infrastructure.Web;
using BitCode.Framework.Shared.Infrastructure.Web.HealthChecks;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.OpenApi;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.IntegrationHub.Api;

/// <summary>
/// Aplicación de referencia de Integration Hub (Fase 6, módulo 9) -- mismo patrón que
/// <c>Sample.Notifications.Api/InfrastructureModule.cs</c> (Fase 6, módulo 8). Tampoco registra
/// <c>AddSharedBackgroundJobs</c>/<c>IntegrationOutboundProcessorJob</c> -- el procesamiento del lote se
/// verifica en <c>Sample.IntegrationHub.Api.Tests</c> invocando el job directamente contra el mismo
/// <see cref="IntegrationHubDbContext"/> (ver <c>docs/guia-integration-hub.md</c>, sección "Colas", para
/// cómo lo agregaría un consumidor productivo con Quartz HA real).
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

        services.AddSharedPersistence<IntegrationHubDbContext>(connectionString);

        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        services.AddSharedAbacAuthorization();
        services.AddSharedAuditing();

        // Requisito documentado de AddSharedIntegrationHub: ISecretProvider ya registrado -- este host
        // usa el proveedor de configuración (F2-12, sin Vault) para no requerir infraestructura externa
        // adicional en el ejemplo de referencia. Ver appsettings.json, sección "Secrets".
        services.AddSharedSecretProvider(configuration);

        services.AddSharedIntegrationHub(
            configureHttpResilience: options => configuration.GetSection("IntegrationHub:HttpResilience").Bind(options),
            configureOptions: options => configuration.GetSection("IntegrationHub:Retry").Bind(options.Retry));

        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.IntegrationHub viven en su propio ensamblado.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(IntegrationHubDbContext).Assembly);
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
