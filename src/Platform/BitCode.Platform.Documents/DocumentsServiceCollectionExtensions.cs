using BitCode.Framework.Platform.Documents.Actors;
using BitCode.Framework.Platform.Documents.Almacenamiento;
using BitCode.Framework.Platform.Documents.Antivirus;
using BitCode.Framework.Platform.Documents.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.Documents;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Documents (Fase 6, módulo 5) -- mismo espíritu que
/// <c>FeatureManagementServiceCollectionExtensions</c>/<c>CatalogsServiceCollectionExtensions</c>. Un
/// consumidor real llama <c>services.AddSharedPersistence&lt;DocumentsDbContext&gt;(connectionString)</c>
/// directamente en su propio <c>InfrastructureModule</c> (ver <c>Sample.Documents.Api</c>), que ya deja
/// resueltos <c>IRepository&lt;,&gt;</c>/<c>IReadRepository&lt;,&gt;</c>/<c>IUnitOfWork</c>/
/// <c>IIdempotencyStore</c>/<c>IInboxStore</c> para las dos entidades de este módulo (genéricos, sin
/// registro adicional por tipo) y el health check "sql-server" de lectura genérico.
/// </summary>
public static class DocumentsServiceCollectionExtensions
{
    /// <summary>
    /// Registra los servicios propios de aplicación del módulo: resolución de actor para
    /// auditoría/ABAC, el health check propio con nombre distintivo, el almacenamiento de blobs
    /// desacoplado (<see cref="IDocumentBlobStore"/>, filesystem local por defecto -- ver
    /// <paramref name="configureBlobStore"/>) y el escaner antivirus de referencia
    /// (<see cref="IAntivirusScanner"/>, reemplazable por un consumidor real). No registra
    /// <c>AddSharedSecurity</c>/<c>AddSharedAbacAuthorization</c>/<c>AddSharedAuditing</c> por su cuenta
    /// -- son responsabilidad explícita del host consumidor (mismo criterio que Catalogs/Feature
    /// Management, ver <c>docs/guia-documents.md</c>, sección "Orden de registro"). Debe llamarse
    /// DESPUÉS de <c>AddSharedPersistence&lt;DocumentsDbContext&gt;</c> (necesita
    /// <see cref="DocumentsDbContext"/> ya registrado para el health check propio).
    /// </summary>
    public static IServiceCollection AddSharedDocuments(
        this IServiceCollection services,
        Action<DocumentBlobStoreOptions> configureBlobStore)
    {
        ArgumentNullException.ThrowIfNull(configureBlobStore);

        services.AddHttpContextAccessor();
        services.TryAddScoped<IDocumentsActorContext, HttpContextDocumentsActorContext>();

        services.AddOptions<DocumentBlobStoreOptions>().Configure(configureBlobStore);
        services.TryAddSingleton<IDocumentBlobStore, FileSystemDocumentBlobStore>();

        // Escaner de referencia (F5-... no aplica, Épica de Documents): un consumidor real reemplaza este
        // registro con Replace()/su propio TryAdd antes de llamar a este método, o directamente
        // registrando su propia implementación de IAntivirusScanner después de este método (el último
        // registro de un servicio no-TryAdd gana).
        services.TryAddSingleton<IAntivirusScanner, ReferenceAntivirusScanner>();

        services.AddHealthChecks()
            .AddCheck<DocumentsDbContextHealthCheck>("sql-server-documents", tags: ["ready"]);

        return services;
    }
}
