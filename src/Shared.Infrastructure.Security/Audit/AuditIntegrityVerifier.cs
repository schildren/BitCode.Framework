namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Implementación por defecto de <see cref="IAuditIntegrityVerifier"/> (F2-16, Épica F2-D). Sin estado
/// propio -- cada llamada a <see cref="Verify"/> es independiente de cualquier otra, por lo que este tipo
/// se registra como singleton (<see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/>) sin ningún
/// requisito adicional de ciclo de vida.
/// </summary>
public sealed class AuditIntegrityVerifier : IAuditIntegrityVerifier
{
    public AuditIntegrityVerificationResult Verify(IReadOnlyList<AuditEntry> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        string? expectedPreviousHash = null;

        for (var index = 0; index < chain.Count; index++)
        {
            var entry = chain[index];

            var recomputedHash = AuditHashCalculator.Compute(
                entry.Id, entry.OccurredAtUtc, entry.Actor, entry.TenantId, entry.Action, entry.Resource,
                entry.Outcome, entry.Reason, entry.CorrelationId, entry.TraceId, entry.IpAddress, entry.Metadata);

            if (!string.Equals(recomputedHash, entry.AuditHash, StringComparison.Ordinal))
            {
                return AuditIntegrityVerificationResult.Invalid(index, entry.Id, AuditIntegrityBreakReason.HashMismatch);
            }

            if (!string.Equals(entry.PreviousAuditHash, expectedPreviousHash, StringComparison.Ordinal))
            {
                return AuditIntegrityVerificationResult.Invalid(index, entry.Id, AuditIntegrityBreakReason.PreviousHashLinkMismatch);
            }

            expectedPreviousHash = entry.AuditHash;
        }

        return AuditIntegrityVerificationResult.Valid();
    }
}
