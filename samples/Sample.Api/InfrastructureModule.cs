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

namespace Sample.Api;

/// <summary>
/// Envuelve los AddSharedX&lt;T&gt;() del framework con los tipos concretos de este proyecto — el
/// patrón documentado en la Fase 5 (Shared.Modularity): IFrameworkModule no reemplaza estos
/// extension methods, los agrupa para que el resto de los módulos de feature (ProductosModule)
/// solo declaren [DependsOn(typeof(InfrastructureModule))] sin repetir la configuración.
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");

        // F1-12: AddHttpContextTenantProvider() ANTES de AddSharedPersistence — registra la
        // implementación productiva de ITenantProvider (resuelve el TenantId desde el claim JWT del
        // request, nunca desde un header/query string) para que gane sobre el NullTenantProvider por
        // defecto (TryAddScoped). Producto (Sample.Api) todavía no implementa ITenantEntity, así que
        // esto no cambia su comportamiento visible hoy: es la referencia de cómo un proyecto
        // consumidor multi-tenant real debe cablear la resolución de tenant.
        services.AddHttpContextTenantProvider();
        services.AddSharedPersistence<SampleDbContext>(connectionString);

        // F1-22: AddHttpContextIdempotencyKeyProvider() ANTES de AddSharedApplication — registra la
        // implementación productiva de IIdempotencyKeyProvider (lee el header Idempotency-Key del
        // request) para que gane sobre el NullIdempotencyKeyProvider por defecto (TryAddScoped),
        // mismo patrón que AddHttpContextTenantProvider() arriba. CrearProductoCommand implementa
        // IIdempotentCommand como referencia de uso end-to-end.
        services.AddHttpContextIdempotencyKeyProvider();
        services.AddSharedApplication(typeof(InfrastructureModule).Assembly);
        services.AddSharedExceptionHandling();

        // F1-27: versionado de API HTTP por segmento de ruta (/api/v{version}/...). Ver
        // ProductosModule para el ApiVersionSet de referencia (v1 deprecada + v2 coexistiendo).
        services.AddSharedApiVersioning();

        // F1-28: un documento OpenAPI POR versión mayor de API (nunca uno mezclando v1 y v2) -- ver
        // docs/guia-openapi.md. Un endpoint HTTP versionado sin ApiVersionMetadata propia aparecería
        // en ambos documentos (AddSharedOpenApiForApiVersion lo trataría como version-neutral); en la
        // práctica, /health/live y /health/ready (MapHealthChecks, F1-25) no llegan siquiera a
        // generar una ApiDescription (MapHealthChecks no pasa por RequestDelegateFactory), así que no
        // aparecen en NINGÚN documento -- ver la nota explícita en docs/guia-openapi.md y
        // OpenApiDocumentsIntegrationTests.NingunDocumento_IncluyeLosEndpointsDeHealthChecks. No se llama
        // OpenApiSecuritySchemeOptionsExtensions.AddJwtBearerSecurityScheme() acá a propósito: ningún
        // endpoint de este proyecto piloto protege con RequireAuthorization/[Authorize] todavía (ver
        // la nota de F1-12 en este mismo archivo) -- documentar un esquema de seguridad que el
        // servidor no exige sería un contrato engañoso. Un consumidor real que sí autentique sus
        // endpoints debe encadenar `.AddJwtBearerSecurityScheme()` al configurar cada documento.
        services.AddSharedOpenApiForApiVersion(1);
        services.AddSharedOpenApiForApiVersion(2);

        // F4-10: Sample.Api no instrumentaba con OpenTelemetry hasta esta tarea -- sin esto, no había
        // NADA que enviarle al OTel Collector introducido acá (k8s/otel-collector/), y "OtlpEndpoint"
        // en el ConfigMap (sample-api-config) habría sido un valor sin efecto. Requiere la sección
        // "OpenTelemetry:ServiceName" en configuración (ver appsettings.json) -- sin
        // "OpenTelemetry:OtlpEndpoint" (dev local sin collector corriendo), las trazas/métricas se
        // siguen generando pero no se exportan a ningún lado (ver
        // ObservabilityServiceCollectionExtensions).
        services.AddSharedObservability(configuration);
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseExceptionHandler();

        // F1-25: /health/live (liveness, sin dependencias externas) y /health/ready (readiness,
        // verifica SQL Server -- y Redis si estuviera configurado) -- ver docs/guia-health-checks.md.
        app.MapSharedHealthChecks();

        // F1-28: expone /openapi/{documentName}.json para TODOS los documentos ya registrados por
        // AddSharedOpenApiForApiVersion (un único MapOpenApi() resuelve /openapi/v1.json y
        // /openapi/v2.json por el route parameter {documentName}) -- ver docs/guia-openapi.md.
        app.MapOpenApi();
    }
}
