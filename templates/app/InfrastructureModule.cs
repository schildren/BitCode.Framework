using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Infrastructure.Observability;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Infrastructure.Web;
using BitCode.Framework.Shared.Infrastructure.Web.HealthChecks;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.OpenApi;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppName;

/// <summary>
/// Envuelve los AddSharedX&lt;T&gt;() del framework con los tipos concretos de este proyecto -- mismo
/// patrón que <c>samples/Sample.Api/InfrastructureModule.cs</c>: agrupa la configuración de
/// infraestructura para que el resto de los módulos de feature solo declaren
/// <c>[DependsOn(typeof(InfrastructureModule))]</c> sin repetirla.
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");

        // Registra la implementación productiva de ITenantProvider ANTES de AddSharedPersistence --
        // resuelve el TenantId desde el claim JWT del request (nunca desde un header/query string). Ver
        // docs/convenciones.md, sección de multi-tenancy.
        services.AddHttpContextTenantProvider();
        services.AddSharedPersistence<AppDbContext>(connectionString);

        services.AddSharedApplication(typeof(InfrastructureModule).Assembly);
        services.AddSharedExceptionHandling();

        // Versionado de API HTTP por segmento de ruta (/api/v{version}/...) desde el día uno -- ver
        // docs/guia-versionado-api.md. Este proyecto base solo declara "v1"; agregar una v2 más adelante
        // no requiere tocar los endpoints existentes (ver samples/Sample.Api/Productos como referencia
        // de coexistencia de versiones).
        services.AddSharedApiVersioning();
        services.AddSharedOpenApiForApiVersion(1);

        services.AddSharedObservability(configuration);
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseExceptionHandler();

        // /health/live (liveness, sin dependencias externas) y /health/ready (readiness, verifica SQL
        // Server) -- ver docs/guia-health-checks.md.
        app.MapSharedHealthChecks();

        // Expone /openapi/v1.json -- ver docs/guia-openapi.md.
        app.MapOpenApi();
    }
}
