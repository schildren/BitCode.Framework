namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Solicitud de escritura de un objeto en <see cref="IWormStorage"/> (F2-18, Épica F2-D): la clave que lo
/// identifica de forma única para siempre, el contenido y el período de retención a partir del instante de
/// escritura. <see cref="RetentionPeriod"/> lo decide el llamador en cada escritura -- esta clase no fija
/// ninguna política concreta (días/años); esa decisión de negocio/regulatoria queda fuera de F2-18 (ver
/// "Qué NO resuelve F2-18" en <c>docs/guia-auditoria-inmutable.md</c>), aunque <see
/// cref="IAuditWormExportPipeline"/> sí aplica un valor por defecto configurable si el llamador no
/// especifica uno explícito.
/// </summary>
public sealed class WormWriteRequest
{
    public WormWriteRequest(string key, byte[] content, TimeSpan retentionPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length == 0)
        {
            throw new ArgumentException("El contenido a escribir en almacenamiento WORM no puede estar vacío.", nameof(content));
        }

        if (retentionPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionPeriod), retentionPeriod, "El período de retención debe ser mayor a cero.");
        }

        Key = key;
        Content = content;
        RetentionPeriod = retentionPeriod;
    }

    /// <summary>Identificador único y permanente del objeto -- nunca reutilizable, ni siquiera tras eliminarlo.</summary>
    public string Key { get; }

    /// <summary>Contenido a persistir de forma inmutable.</summary>
    public byte[] Content { get; }

    /// <summary>Período durante el cual el objeto no puede eliminarse, contado desde el instante de escritura.</summary>
    public TimeSpan RetentionPeriod { get; }
}
