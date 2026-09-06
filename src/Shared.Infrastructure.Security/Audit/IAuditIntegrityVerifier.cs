namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Servicio de integridad de la cadena de auditoría (F2-16, Épica F2-D, entregable "Servicio de
/// integridad"). Recorre una secuencia de <see cref="AuditEntry"/> de una misma cadena -- ver
/// <see cref="AuditEntry.PreviousAuditHash"/> para la definición de "misma cadena" (por tenant) -- ya
/// ordenada cronológicamente (mismo orden en el que <see cref="IAuditWriter"/> las escribió) y determina si
/// hay manipulación detectable. Es una operación puramente de cómputo sobre datos ya provistos por el
/// llamador -- no lee de ningún almacenamiento por sí mismo (no depende de <see cref="IAuditWriter"/> ni de
/// ninguna forma de persistencia), para no acoplar el servicio de integridad a una implementación concreta
/// de lectura (que es F2-20).
/// </summary>
public interface IAuditIntegrityVerifier
{
    /// <summary>
    /// Verifica la integridad de <paramref name="chain"/>. Detecta dos tipos de manipulación (ver
    /// <see cref="AuditIntegrityBreakReason"/>): un registro modificado después de escrito (su
    /// <see cref="AuditEntry.AuditHash"/> ya no coincide con sus campos actuales) y un registro eliminado o
    /// la secuencia reordenada (el <see cref="AuditEntry.PreviousAuditHash"/> de un registro no coincide con
    /// el <see cref="AuditEntry.AuditHash"/> del anterior en la secuencia dada). Una secuencia vacía es
    /// válida por definición (no hay nada que verificar); una secuencia de un único registro es válida si su
    /// <see cref="AuditEntry.PreviousAuditHash"/> es <see langword="null"/> (registro génesis de la cadena)
    /// y su propio hash coincide.
    /// </summary>
    /// <param name="chain">
    /// Registros de una misma cadena de auditoría (mismo <see cref="AuditEntry.TenantId"/>), en el mismo
    /// orden en el que fueron escritos. Este método no agrupa ni reordena por su cuenta -- es
    /// responsabilidad del llamador entregar ya la secuencia correcta de una única cadena.
    /// </param>
    AuditIntegrityVerificationResult Verify(IReadOnlyList<AuditEntry> chain);
}
