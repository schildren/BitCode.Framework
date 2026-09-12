namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Opciones de F2-18 (sección de configuración <see cref="SectionName"/>) para <see
/// cref="AuditWormExportPipeline"/>. <see cref="RetentionPeriod"/> es el valor por defecto aplicado cuando
/// un llamador de <see cref="IAuditWormExportPipeline.ExportAsync"/> no especifica explícitamente uno
/// distinto para un lote puntual -- el default (7 años) es un placeholder razonable inspirado en
/// retenciones contables/regulatorias comunes, NO una recomendación normativa: qué período corresponde
/// exactamente a cada proyecto/regulación (SOX, PCI-DSS, normativa local de cada industria) es una decisión
/// de negocio/cumplimiento que queda explícitamente fuera de alcance de F2-18 (ver "Qué NO resuelve F2-18"
/// en <c>docs/guia-auditoria-inmutable.md</c>) -- cada proyecto consumidor debe fijar el valor que
/// corresponda a su propio marco regulatorio.
/// </summary>
public sealed class AuditWormExportOptions
{
    public const string SectionName = "AuditWormExport";

    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(365 * 7);
}
