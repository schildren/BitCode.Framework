namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Solicitud de exportación de un lote de auditoría ya escrito a <see cref="IWormStorage"/> (F2-18, Épica
/// F2-D) a través de <see cref="IAuditWormExportPipeline"/>.
/// </summary>
public sealed class AuditWormExportRequest
{
    public AuditWormExportRequest(
        string key, IReadOnlyList<AuditEntry> batch, AuditBatchSignature? signature = null, TimeSpan? retentionPeriod = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(batch);

        Key = key;
        Batch = batch;
        Signature = signature;
        RetentionPeriod = retentionPeriod;
    }

    /// <summary>
    /// Clave del objeto WORM resultante -- responsabilidad del llamador construirla de forma única y
    /// determinística (por ejemplo, <c>"{tenantId}/{fecha}/{idDelPrimerRegistro}"</c>); este pipeline no
    /// agrupa ni genera claves automáticamente, mismo criterio que <see cref="IAuditBatchSigner"/> (F2-17)
    /// no agrupa lotes automáticamente.
    /// </summary>
    public string Key { get; }

    public IReadOnlyList<AuditEntry> Batch { get; }

    /// <summary>
    /// Firma de F2-17 sobre <see cref="Batch"/>, si el llamador decidió firmarlo antes de exportarlo --
    /// opcional: exportar sin firma es válido, la integración con F2-17 es decisión del llamador, no
    /// obligatoria (ver "Integración con F2-17" en <c>docs/guia-auditoria-inmutable.md</c>).
    /// </summary>
    public AuditBatchSignature? Signature { get; }

    /// <summary>
    /// Período de retención para este lote puntual. <see langword="null"/> usa el valor por defecto de
    /// <see cref="AuditWormExportOptions.RetentionPeriod"/>.
    /// </summary>
    public TimeSpan? RetentionPeriod { get; }
}
