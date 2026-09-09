using BitCode.Framework.Platform.Documents;
using BitCode.Framework.Platform.Documents.Almacenamiento;
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

namespace Sample.Documents.Api;

/// <summary>
/// Aplicación de referencia de Documents (Fase 6, módulo 5) -- mismo patrón que
/// <c>Sample.FeatureManagement.Api/InfrastructureModule.cs</c>: agrupa los AddSharedX&lt;T&gt;() del
/// framework y de la librería BitCode.Platform.Documents.
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
        // -- Documento/DocumentoVersion implementan ITenantEntity.
        services.AddHttpContextTenantProvider();

        // DocumentsDbContext hereda directamente de MultiTenantDbContext (mismo criterio que
        // FeatureManagementDbContext/CatalogsDbContext) -- por eso este módulo usa AddSharedPersistence<T>
        // genérico tal cual.
        services.AddSharedPersistence<DocumentsDbContext>(connectionString);

        // SampleIdentityDbContext es un DbContext de identidad PROPIO de este host de referencia (base de
        // datos separada, ConnectionStrings:Identity) -- Documents no es dueño de usuarios/roles.
        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        // F2-08/F2-10: ABAC -- requerido por SubirVersionDocumentoCommandHandler/
        // DescargarDocumentoVersionQueryHandler/DisponerDocumentoCommandHandler. Un consumidor real
        // configura acá sus propias reglas de alcance (ScopeRules) sobre "documents.documentos" -- ver
        // docs/guia-documents.md, sección "RBAC + ABAC por documento". Este proyecto de referencia no
        // agrega ninguna regla de alcance por defecto (lista vacía = sin restricción adicional más allá de
        // RBAC), documentado explícitamente en la guía.
        services.AddSharedAbacAuthorization();

        // F2-15: auditoría (InMemoryAuditWriter, placeholder de referencia).
        services.AddSharedAuditing();

        // Épica de Documents, "Integración con almacenamiento desacoplado": filesystem local, directorio
        // configurable vía Documents:BlobStore:RootPath.
        services.AddSharedDocuments(options => configuration.GetSection("Documents:BlobStore").Bind(options));

        // F1-22: ANTES de AddSharedApplication -- resuelve IIdempotencyKeyProvider desde el header HTTP
        // "Idempotency-Key" del request.
        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.Documents viven en su propio ensamblado -- deben sumarse
        // explícitamente al descubrimiento de MediatR/FluentValidation de este host.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(DocumentsDbContext).Assembly);
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
