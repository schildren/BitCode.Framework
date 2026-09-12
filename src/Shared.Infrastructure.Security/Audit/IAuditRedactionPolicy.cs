namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Clasifica y redacta datos potencialmente sensibles de un <see cref="AuditEntryRequest"/> ANTES de que
/// llegue a <see cref="IAuditWriter.WriteAsync"/> real (F2-19, Épica F2-D). Interfaz propia -- no un método
/// estático -- por el mismo motivo que <see cref="IAuditBatchSigner"/> (F2-17): un proyecto consumidor con
/// una política de clasificación de PII propia (por ejemplo, integrada con una herramienta de DLP externa)
/// puede reemplazar la implementación por defecto (<see cref="AuditRedactionPolicy"/>) registrando la suya
/// después de <see cref="AuditRedactionServiceCollectionExtensions.AddSharedAuditRedaction"/>, sin tocar
/// <see cref="RedactingAuditWriter"/>.
/// </summary>
public interface IAuditRedactionPolicy
{
    /// <summary>
    /// Devuelve un <see cref="AuditEntryRequest"/> nuevo con los valores sensibles de
    /// <see cref="AuditEntryRequest.Metadata"/>/<see cref="AuditEntryRequest.Reason"/> reemplazados -- nunca
    /// muta <paramref name="request"/> (es inmutable, igual que el resto de los tipos de este módulo).
    /// </summary>
    AuditEntryRequest Redact(AuditEntryRequest request);
}
