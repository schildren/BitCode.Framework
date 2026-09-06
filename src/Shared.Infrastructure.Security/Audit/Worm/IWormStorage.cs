namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

using BitCode.Framework.Shared.Kernel;

/// <summary>
/// Abstracción intercambiable de almacenamiento con semántica WORM (Write-Once-Read-Many), introducida
/// por F2-18 (Épica F2-D, entregable "Pipeline de retención", criterio de aceptación "Escritura y lectura
/// probadas") -- el framework no exponía previamente ninguna abstracción de blob/object storage que F2-18
/// pudiera reutilizar (no existe ningún <c>IBlobStorage</c>/<c>IObjectStorage</c> anterior en
/// <c>Shared.Infrastructure.*</c>), así que esta interfaz nace con esta tarea.
/// <para>
/// Deliberadamente genérica ("clave -&gt; bytes", sin conocer nada de <see cref="AuditEntry"/>) siguiendo el
/// mismo patrón que <see cref="Secrets.ISecretProvider"/> (F2-12) y <see cref="Encryption.IEncryptionProvider"/>
/// (F2-13): el código que decide QUÉ exportar y con qué política de retención (<see
/// cref="IAuditWormExportPipeline"/>) es una capa distinta de CÓMO se persiste de forma inmutable. Un
/// proyecto consumidor que ya tenga otro caso de uso de almacenamiento inmutable (no solo auditoría) puede
/// reutilizar esta misma interfaz.
/// </para>
/// <para>
/// Semántica WORM que toda implementación debe respetar:
/// <list type="bullet">
/// <item><b>Write-once real, no solo durante la retención</b>: <see cref="WriteAsync"/> con una clave ya
/// usada anteriormente falla SIEMPRE (<c>Worm.ObjectAlreadyExists</c>), incluso si el objeto original ya
/// fue eliminado tras expirar su retención -- permitir reescribir una clave ya usada abriría la puerta a
/// fabricar un reemplazo bajo el mismo identificador que un registro de auditoría legítimo ya eliminado,
/// lo que rompería la garantía de inmutabilidad frente a quien solo conoce la clave.</item>
/// <item><b>Retención bloquea eliminación, no lectura</b>: <see cref="DeleteAsync"/> antes de que expire
/// <see cref="WormObjectMetadata.RetentionExpiresAtUtc"/> falla con un error de NEGOCIO
/// (<c>Worm.RetentionPeriodNotExpired</c>, <see cref="ErrorType.Conflict"/>), nunca con una excepción no
/// controlada -- un intento de eliminación prematura es un resultado esperado a evaluar por el llamador
/// (por ejemplo, para registrar el intento como un evento de seguridad), no una condición excepcional.
/// <see cref="ReadAsync"/> nunca depende del estado de retención: un objeto con retención vigente o vencida
/// (mientras no haya sido eliminado) se lee exactamente igual.</item>
/// <item><b>Eliminación después de expirar la retención SÍ es válida</b> -- WORM no significa "eliminación
/// prohibida para siempre", significa "eliminación prohibida mientras la retención esté vigente". Cumplir
/// una política de expurgo/derecho al olvido después de que venció el período de retención legal/regulatorio
/// exigido sigue siendo una operación soportada.</item>
/// </list>
/// </para>
/// </summary>
public interface IWormStorage
{
    /// <summary>
    /// Escribe <paramref name="request"/> bajo su <see cref="WormWriteRequest.Key"/>, con la retención
    /// indicada (<see cref="WormWriteRequest.RetentionPeriod"/>) a partir del instante de escritura. Falla
    /// con <c>Worm.ObjectAlreadyExists</c> (<see cref="ErrorType.Conflict"/>) si la clave ya fue usada
    /// anteriormente -- ver "Write-once real" en la documentación de este tipo.
    /// </summary>
    Task<Result<WormObjectMetadata>> WriteAsync(WormWriteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee de vuelta el contenido íntegro escrito bajo <paramref name="key"/>. Falla con
    /// <c>Worm.ObjectNotFound</c> (<see cref="ErrorType.NotFound"/>) si la clave nunca existió o si el
    /// objeto ya fue eliminado (después de expirar su retención). Independiente del estado de retención
    /// mientras el objeto no haya sido eliminado.
    /// </summary>
    Task<Result<WormObject>> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina el objeto identificado por <paramref name="key"/>. Falla con
    /// <c>Worm.RetentionPeriodNotExpired</c> (<see cref="ErrorType.Conflict"/>) si todavía no expiró <see
    /// cref="WormObjectMetadata.RetentionExpiresAtUtc"/>, o con <c>Worm.ObjectNotFound</c> (<see
    /// cref="ErrorType.NotFound"/>) si la clave no existe o ya fue eliminada. Una vez eliminada, la clave
    /// queda permanentemente inutilizable para <see cref="WriteAsync"/> (ver "Write-once real" arriba).
    /// </summary>
    /// <remarks>
    /// Esta primitiva NO aplica ninguna autorización propia sobre quién puede purgar un objeto tras
    /// expirar su retención -- mismo criterio que <see cref="Secrets.ISecretProvider"/>/<see
    /// cref="Encryption.IEncryptionProvider"/>, que tampoco autorizan por sí solos. Cumplir la retención
    /// (bloquear el borrado mientras esté vigente) es responsabilidad de esta interfaz; decidir QUIÉN
    /// puede invocar <see cref="DeleteAsync"/> una vez vencida (RBAC/ABAC, y auditar el propio intento de
    /// purga) es responsabilidad exclusiva de la capa que orquesta <see cref="IAuditWormExportPipeline"/>
    /// o que llama a esta interfaz directamente -- esta pieza de bajo nivel no la impone.
    /// </remarks>
    Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
