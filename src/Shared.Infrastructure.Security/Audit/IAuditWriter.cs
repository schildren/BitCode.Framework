using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Abstracción de escritura de auditoría (F2-15, Épica F2-D). Deliberadamente expone un único método de
/// escritura y ninguno de actualización/eliminación -- ese es el mecanismo append-only a nivel de API del
/// propio módulo que exige el criterio de aceptación de F2-15, independiente de cualquier restricción que
/// además se configure a nivel de base de datos (permisos de esquema, triggers, WORM del destino de F2-18).
/// Un proyecto consumidor que necesite auditoría persistente real (tabla SQL append-only, event store, o
/// el destino WORM de F2-18) registra su propia implementación DESPUÉS de llamar a
/// <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/> -- el último registro para este mismo
/// tipo de servicio gana la resolución (mismo principio que <c>AddSharedPermissionEvaluation</c>, F2-07).
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Persiste una nueva entrada de auditoría. Nunca lanza una excepción de negocio esperada -- un fallo
    /// transitorio del almacenamiento subyacente se modela como <see cref="Result{TValue}"/> fallido
    /// (mismo patrón que <c>ISecretProvider</c>/<c>IEncryptionProvider</c>, F2-12/F2-13), para que el
    /// llamador decida explícitamente si un fallo al auditar debe bloquear la operación de negocio que
    /// intentaba auditar o solo quedar registrado en logs.
    /// </summary>
    Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default);
}
