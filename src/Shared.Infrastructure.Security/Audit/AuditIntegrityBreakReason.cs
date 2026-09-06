namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Tipo de ruptura de integridad detectada por <see cref="IAuditIntegrityVerifier"/> (F2-16, Épica F2-D).
/// Los dos tipos de manipulación que la cadena de integridad puede detectar, correspondientes a los dos
/// hashes independientes que participan (el hash del propio registro y el enlace al registro anterior):
/// </summary>
public enum AuditIntegrityBreakReason
{
    /// <summary>
    /// El <see cref="AuditEntry.AuditHash"/> almacenado de un registro no coincide con el hash recalculado
    /// (<see cref="AuditHashCalculator.Compute"/>) sobre sus campos actuales -- ese registro fue modificado
    /// después de escrito.
    /// </summary>
    HashMismatch,

    /// <summary>
    /// El <see cref="AuditEntry.PreviousAuditHash"/> de un registro no coincide con el
    /// <see cref="AuditEntry.AuditHash"/> del registro inmediatamente anterior en la secuencia provista (o,
    /// para el primer registro de la secuencia, no es <see langword="null"/>) -- un registro completo fue
    /// eliminado de la cadena, la secuencia fue reordenada, o el enlace fue reescrito directamente.
    /// </summary>
    PreviousHashLinkMismatch,
}
