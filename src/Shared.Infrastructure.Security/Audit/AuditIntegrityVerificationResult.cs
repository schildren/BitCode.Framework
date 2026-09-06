namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Resultado de <see cref="IAuditIntegrityVerifier.Verify"/> (F2-16, Épica F2-D). Deliberadamente no es un
/// simple <see langword="bool"/>: cuando la cadena no es íntegra, expone en qué posición y en qué registro
/// se detectó la ruptura y de qué tipo fue -- "manipulación detectable" (criterio de aceptación de F2-16)
/// implica poder diagnosticar la manipulación, no solo saber que existió.
/// </summary>
public sealed class AuditIntegrityVerificationResult
{
    private AuditIntegrityVerificationResult(
        bool isValid,
        int? brokenAtIndex,
        Guid? brokenEntryId,
        AuditIntegrityBreakReason? reason)
    {
        IsValid = isValid;
        BrokenAtIndex = brokenAtIndex;
        BrokenEntryId = brokenEntryId;
        Reason = reason;
    }

    /// <summary>La cadena provista es íntegra de punta a punta (incluye el caso de una secuencia vacía).</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Posición (índice base cero dentro de la secuencia provista a <see cref="IAuditIntegrityVerifier.Verify"/>)
    /// del primer registro en el que se detectó una ruptura. <see langword="null"/> si <see cref="IsValid"/>
    /// es <see langword="true"/>.
    /// </summary>
    public int? BrokenAtIndex { get; }

    /// <summary><see cref="AuditEntry.Id"/> del registro en el que se detectó la ruptura, o <see langword="null"/> si <see cref="IsValid"/> es <see langword="true"/>.</summary>
    public Guid? BrokenEntryId { get; }

    /// <summary>Tipo de ruptura detectada, o <see langword="null"/> si <see cref="IsValid"/> es <see langword="true"/>.</summary>
    public AuditIntegrityBreakReason? Reason { get; }

    public static AuditIntegrityVerificationResult Valid() => new(true, null, null, null);

    public static AuditIntegrityVerificationResult Invalid(int brokenAtIndex, Guid brokenEntryId, AuditIntegrityBreakReason reason) =>
        new(false, brokenAtIndex, brokenEntryId, reason);
}
