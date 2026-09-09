using BitCode.Framework.Platform.ImportExport.Actors;
using BitCode.Framework.Platform.ImportExport.Almacenamiento;
using BitCode.Framework.Platform.ImportExport.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Platform.ImportExport;

/// <summary>
/// Envuelve el cableado de aplicación del módulo Import and Export (Fase 6, módulo 10) -- mismo espíritu
/// que <c>IntegrationHubServiceCollectionExtensions</c>. Un consumidor real llama
/// <c>services.AddSharedPersistence&lt;ImportExportDbContext&gt;(connectionString)</c> directamente en su
/// propio <c>InfrastructureModule</c> (ver <c>Sample.ImportExport.Api</c>).
/// </summary>
/// <remarks>
/// <b>NO registra ninguna implementación de <see cref="Importacion.IImportRowHandler"/>/
/// <see cref="Exportacion.IExportDataSource"/></b> -- ambas son puntos de extensión específicos del
/// negocio del consumidor (ver sus respectivos <c>remarks</c>); este método solo cablea la infraestructura
/// genérica del módulo (almacenamiento transitorio, health check, contexto de actor). El host es
/// responsable de registrar sus propias implementaciones antes de que
/// <c>Procesamiento.ImportBatchProcessorJob</c>/<c>ExportBatchProcessorJob</c> las necesiten.
/// </remarks>
public static class ImportExportServiceCollectionExtensions
{
    /// <param name="configureOptions">Configuración opcional de <see cref="ImportExportOptions"/> (tamaño
    /// de lote) -- si no se provee, aplican los valores por defecto.</param>
    /// <param name="configureFileStore">Configuración opcional de
    /// <see cref="ImportExportFileStoreOptions"/> (directorio raíz del almacenamiento transitorio de
    /// referencia) -- obligatoria en la práctica: sin <see cref="ImportExportFileStoreOptions.RootPath"/>
    /// configurado, <see cref="FileSystemImportExportFileStore"/> falla en el primer uso.</param>
    public static IServiceCollection AddSharedImportExport(
        this IServiceCollection services,
        Action<ImportExportOptions>? configureOptions = null,
        Action<ImportExportFileStoreOptions>? configureFileStore = null)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IImportExportActorContext, HttpContextImportExportActorContext>();

        services.AddOptions<ImportExportOptions>().Configure(configureOptions ?? (_ => { }));
        services.AddOptions<ImportExportFileStoreOptions>().Configure(configureFileStore ?? (_ => { }));
        services.TryAddScoped<IImportExportFileStore, FileSystemImportExportFileStore>();

        services.AddHealthChecks()
            .AddCheck<ImportExportDbContextHealthCheck>("sql-server-importexport", tags: ["ready"]);

        return services;
    }
}
