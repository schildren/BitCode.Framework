using System.Text.Json;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Serialización del contenido que <see cref="AuditWormExportPipeline"/> escribe en <see
/// cref="IWormStorage"/> (F2-18, Épica F2-D): el lote de <see cref="AuditEntry"/> más, opcionalmente, la
/// representación serializada (<see cref="AuditBatchSignature.ToString"/>) de la firma de F2-17 que lo
/// acompaña -- ver "Integración con F2-17" en <c>docs/guia-auditoria-inmutable.md</c> para la decisión de
/// diseño de exportar datos y firma juntos en un mismo objeto WORM, en lugar de mantenerlos desacoplados.
/// Pública (no interna) para que un proyecto que implemente su propio <see cref="IWormStorage"/> distinto
/// de <see cref="InMemoryWormStorage"/>, o que necesite leer un objeto WORM ya exportado fuera de <see
/// cref="AuditWormExportPipeline"/> (por ejemplo, la futura API de lectura de F2-20), pueda reutilizar
/// exactamente el mismo formato sin reinventar la serialización.
/// </summary>
public static class AuditWormBatchSerializer
{
    private sealed class Payload
    {
        public IReadOnlyList<AuditEntry> Entries { get; set; } = Array.Empty<AuditEntry>();

        public string? Signature { get; set; }
    }

    /// <summary>
    /// Serializa <paramref name="batch"/> (y, si se provee, <paramref name="signature"/>) a JSON UTF-8.
    /// </summary>
    public static byte[] Serialize(IReadOnlyList<AuditEntry> batch, AuditBatchSignature? signature)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var payload = new Payload { Entries = batch, Signature = signature?.ToString() };
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    /// <summary>
    /// Reconstruye el lote (y la firma, si el objeto la incluía) a partir del contenido producido por <see
    /// cref="Serialize"/>. Lanza <see cref="InvalidOperationException"/> (nunca una excepción de
    /// deserialización sin traducir) ante contenido vacío, corrupto, o con una firma embebida con formato
    /// inválido -- <see cref="AuditWormExportPipeline.ReadAsync"/> traduce esto a un <c>Result</c> fallido.
    /// </summary>
    public static (IReadOnlyList<AuditEntry> Batch, AuditBatchSignature? Signature) Deserialize(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("El contenido del objeto WORM no es un lote de auditoría serializado válido.", ex);
        }

        if (payload is null)
        {
            throw new InvalidOperationException("El contenido del objeto WORM está vacío.");
        }

        AuditBatchSignature? signature = null;
        if (payload.Signature is not null)
        {
            if (!AuditBatchSignature.TryParse(payload.Signature, out signature))
            {
                throw new InvalidOperationException("La firma embebida en el objeto WORM no tiene un formato válido.");
            }
        }

        return (payload.Entries, signature);
    }
}
