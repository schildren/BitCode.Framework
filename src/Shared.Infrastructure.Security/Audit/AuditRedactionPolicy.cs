using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Implementación por defecto de <see cref="IAuditRedactionPolicy"/> (F2-19, Épica F2-D). Combina dos
/// mecanismos, deliberadamente en este orden de confiabilidad:
/// <list type="number">
/// <item><description><b>Clasificación por nombre de clave</b> (<see
/// cref="AuditRedactionOptions.SensitiveMetadataKeys"/>): la política PRINCIPAL. Toda clave de
/// <see cref="AuditEntryRequest.Metadata"/> que matchee (case-insensitive) se redacta siempre, sin importar
/// su contenido.</description></item>
/// <item><description><b>Detección de patrones de contenido</b> (si <see
/// cref="AuditRedactionOptions.EnableContentPatternDetection"/> está habilitado, default): una red de
/// seguridad adicional aplicada tanto a valores de <c>Metadata</c> cuya clave NO fue declarada sensible como
/// a <see cref="AuditEntryRequest.Reason"/> (que no es un diccionario -- no tiene "clave" que
/// clasificar).</description></item>
/// </list>
/// <para>
/// <b>Patrones cubiertos, y por qué exactamente estos dos</b>: direcciones de email
/// (<see cref="EmailPattern"/>) y secuencias largas de dígitos compatibles con un número de
/// documento/tarjeta (<see cref="LongDigitSequencePattern"/>, 13 a 19 dígitos contiguos, el rango típico de
/// un PAN de tarjeta -- ISO/IEC 7812 -- y de varios documentos de identidad numéricos largos). Un patrón de
/// teléfono NO se incluyó a propósito: el formato de un número de teléfono varía demasiado entre países
/// (con/sin prefijo internacional, con/sin separadores) para un patrón único que no genere una tasa alta de
/// falsos positivos/negativos -- se prefirió no incluir un patrón de baja confiabilidad antes que dar una
/// falsa sensación de cobertura. Ver "Qué NO resuelve F2-19" en <c>docs/guia-auditoria-inmutable.md</c> para
/// el resto de las limitaciones explícitas de este mecanismo (es <b>best-effort</b>, nunca una garantía).
/// </para>
/// </summary>
public sealed class AuditRedactionPolicy(IOptions<AuditRedactionOptions> options) : IAuditRedactionPolicy
{
    // RegexOptions.Compiled: esta política corre en el camino de escritura de CADA entrada de auditoría
    // (RedactingAuditWriter), el mismo criterio de costo que ya aplica AuditHashCalculator sobre cada
    // escritura -- vale la pena pagar el costo de compilación una sola vez a cambio de evaluación más
    // rápida en el caso común (muchas escrituras, patrones fijos).
    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 13-19 dígitos contiguos, con o sin separadores de espacio/guion cada 4 -- cubre tanto un PAN sin
    // formatear (p. ej. "4111111111111111") como el formato habitual con separadores
    // (p. ej. "4111-1111-1111-1111" o "4111 1111 1111 1111"). No exige Luhn: un chequeo de Luhn reduciría
    // falsos positivos pero también es una validación adicional que esta tarea no necesita para cumplir su
    // criterio de aceptación ("logs sin PII no autorizada") -- ver limitación documentada arriba.
    private static readonly Regex LongDigitSequencePattern = new(
        @"\b\d(?:[ \-]?\d){12,18}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public AuditEntryRequest Redact(AuditEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var opts = options.Value;
        var placeholder = opts.RedactionPlaceholder;
        var sensitiveKeys = new HashSet<string>(opts.SensitiveMetadataKeys, StringComparer.OrdinalIgnoreCase);

        var redactedMetadata = new Dictionary<string, string?>(request.Metadata.Count);
        foreach (var pair in request.Metadata)
        {
            if (sensitiveKeys.Contains(pair.Key))
            {
                redactedMetadata[pair.Key] = placeholder;
                continue;
            }

            redactedMetadata[pair.Key] = opts.EnableContentPatternDetection
                ? RedactMatchedPatterns(pair.Value, placeholder)
                : pair.Value;
        }

        var redactedReason = opts.EnableContentPatternDetection
            ? RedactMatchedPatterns(request.Reason, placeholder)
            : request.Reason;

        return new AuditEntryRequest(
            request.Actor,
            request.TenantId,
            request.Action,
            request.Resource,
            request.Outcome,
            redactedReason,
            request.CorrelationId,
            request.TraceId,
            request.IpAddress,
            redactedMetadata);
    }

    private static string? RedactMatchedPatterns(string? value, string placeholder)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (EmailPattern.IsMatch(value) || LongDigitSequencePattern.IsMatch(value))
        {
            return placeholder;
        }

        return value;
    }
}
