namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Opciones de F2-19 (sección de configuración <see cref="SectionName"/>) para <see
/// cref="AuditRedactionPolicy"/>. "Qué es PII" varía por jurisdicción y dominio de negocio -- por eso la
/// clasificación principal (<see cref="SensitiveMetadataKeys"/>) es explícita y configurable por proyecto,
/// no un listado fijo del framework. Los defaults de esta clase son un punto de partida razonable (nombres
/// de clave habituales en <c>AuditEntryRequest.Metadata</c> que suelen cargar un dato sensible), NO una
/// clasificación normativa ni exhaustiva de ninguna jurisdicción concreta -- cada proyecto consumidor debe
/// revisar y extender esta lista según su propio marco regulatorio y su propio uso de <c>Metadata</c> (ver
/// "Qué NO resuelve F2-19" en <c>docs/guia-auditoria-inmutable.md</c>).
/// </summary>
public sealed class AuditRedactionOptions
{
    public const string SectionName = "AuditRedaction";

    /// <summary>
    /// Nombres de clave de <c>AuditEntryRequest.Metadata</c> considerados sensibles -- comparación
    /// case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>). Es la clasificación PRINCIPAL de
    /// F2-19: declarar acá una clave es la forma confiable de decirle a <see cref="AuditRedactionPolicy"/>
    /// "este valor nunca debe persistir en texto plano", independientemente de si su contenido matchea o no
    /// algún patrón reconocido por <see cref="EnableContentPatternDetection"/>.
    /// </summary>
    public IList<string> SensitiveMetadataKeys { get; set; } =
    [
        "password",
        "contraseña",
        "clave",
        "secret",
        "token",
        "apikey",
        "api_key",
        "ssn",
        "dni",
        "cuit",
        "cuil",
        "creditcard",
        "credit_card",
        "tarjeta",
        "cvv",
        "email",
        "correo",
        "phone",
        "telefono",
        "teléfono",
    ];

    /// <summary>
    /// Habilita la detección de patrones de PII en el CONTENIDO de <c>Metadata</c> (valores de claves no
    /// declaradas en <see cref="SensitiveMetadataKeys"/>) y de <c>Reason</c> -- una red de seguridad
    /// adicional (<i>defense in depth</i>) contra un desarrollador que vuelque un dato sensible bajo una
    /// clave no prevista, no la clasificación principal. Ver <see cref="AuditRedactionPolicy"/> para el
    /// detalle de qué patrones cubre y sus límites explícitos -- es un mecanismo <b>best-effort</b>, nunca
    /// una garantía de remoción completa de PII no declarada.
    /// </summary>
    public bool EnableContentPatternDetection { get; set; } = true;

    /// <summary>
    /// Texto con el que <see cref="AuditRedactionPolicy"/> reemplaza un valor clasificado como sensible.
    /// Un placeholder fijo (no un hash truncado ni ningún otro esquema reversible/correlacionable) es
    /// deliberado: el criterio de aceptación de F2-19 es "logs sin PII no autorizada", no "PII
    /// pseudonimizada pero igual reconstruible" -- si un caso de uso futuro necesitara correlacionar
    /// valores redactados entre sí sin exponerlos, eso es una extensión posterior explícita, no el mínimo
    /// que esta tarea resuelve.
    /// </summary>
    public string RedactionPlaceholder { get; set; } = "[REDACTED]";
}
