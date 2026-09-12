using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Pipeline de exportación de lotes de auditoría a almacenamiento WORM (F2-18, Épica F2-D, entregable
/// "Pipeline de retención"): serializa un lote de <see cref="AuditEntry"/> ya escrito (opcionalmente junto
/// con su <see cref="AuditBatchSignature"/> de F2-17) y lo persiste a través de <see cref="IWormStorage"/>
/// con la política de retención configurada (<see cref="AuditWormExportOptions"/>) -- esta es la pieza
/// adicional que convierte la primitiva genérica <see cref="IWormStorage"/> ("clave -&gt; bytes con
/// retención") en el entregable concreto que F2-18 pide: exportar auditoría con una política de retención
/// centralizada, en lugar de que cada llamador arme y calcule la retención por su cuenta en cada lote.
/// </summary>
public interface IAuditWormExportPipeline
{
    /// <summary>
    /// Serializa y exporta <paramref name="request"/>. Falla con <c>AuditWormExport.EmptyBatch</c> si el
    /// lote está vacío, o con el error que devuelva <see cref="IWormStorage.WriteAsync"/> (por ejemplo,
    /// <c>Worm.ObjectAlreadyExists</c> si la clave ya fue usada).
    /// </summary>
    Task<Result<WormObjectMetadata>> ExportAsync(AuditWormExportRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee de vuelta y deserializa el lote exportado bajo <paramref name="key"/>, junto con su firma si la
    /// tenía. Falla con <c>Worm.ObjectNotFound</c> si la clave no existe o ya fue eliminada, o con
    /// <c>AuditWormExport.DeserializationFailed</c> si el contenido no corresponde al formato de <see
    /// cref="AuditWormBatchSerializer"/>.
    /// </summary>
    Task<Result<AuditWormExportedBatch>> ReadAsync(string key, CancellationToken cancellationToken = default);
}
