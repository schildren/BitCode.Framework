using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Registra <see cref="IWormStorage"/> e <see cref="IAuditWormExportPipeline"/> (F2-18, Épica F2-D).
/// Deliberadamente un método de registro SEPARADO de <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/>
/// y de <see cref="AuditBatchSigningServiceCollectionExtensions.AddSharedAuditBatchSigning"/> -- exportar a
/// WORM es opt-in (requiere que el proyecto consumidor haya decidido su propia política de "cuándo exportar
/// un lote", igual que F2-17 decide "cuándo firmarlo") mientras que <c>AddSharedAuditing</c> no requiere
/// ninguna configuración adicional para dejar auditoría básica funcionando.
/// </summary>
public static class AuditWormExportServiceCollectionExtensions
{
    /// <summary>
    /// Lee la sección <see cref="AuditWormExportOptions.SectionName"/> (opcional: el default de <see
    /// cref="AuditWormExportOptions.RetentionPeriod"/> ya es válido sin configuración explícita) y registra
    /// <see cref="InMemoryWormStorage"/> como implementación por defecto de <see cref="IWormStorage"/>
    /// (<c>TryAddSingleton</c> -- mismo criterio que <see cref="InMemoryAuditWriter"/>: el almacenamiento en
    /// memoria necesita sobrevivir al scope de un request individual) y <see cref="AuditWormExportPipeline"/>
    /// como implementación de <see cref="IAuditWormExportPipeline"/>. Un proyecto consumidor que necesite un
    /// destino WORM real (por ejemplo, MinIO/S3 Object Lock o Azure Blob Storage con Immutable Storage --
    /// ver ADR 0017, `Proposed`) registra su propia implementación de <see cref="IWormStorage"/> DESPUÉS de
    /// este método -- gana la resolución, mismo principio que <c>AddSharedAuditing</c>.
    /// </summary>
    public static IServiceCollection AddSharedAuditWormExport(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuditWormExportOptions>(configuration.GetSection(AuditWormExportOptions.SectionName));
        services.TryAddSingleton<IWormStorage, InMemoryWormStorage>();
        services.TryAddSingleton<IAuditWormExportPipeline, AuditWormExportPipeline>();
        return services;
    }
}
