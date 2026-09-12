using System.Globalization;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Firma criptográfica de F2-17 (Épica F2-D) sobre un lote de <see cref="AuditEntry"/> ya escrito, emitida
/// por <see cref="IAuditBatchSigner.SignAsync"/>. Cubre tanto el contenido del lote como el instante en el
/// que se firmó (<see cref="SignedAtUtc"/>) -- el "timestamp" del entregable de F2-17 ("Firma y timestamp"):
/// dos lotes idénticos firmados en instantes distintos producen firmas distintas, porque el instante forma
/// parte del material firmado (ver <see cref="HmacAuditBatchSigner"/>), lo que evita que una firma antigua
/// se reutilice para "fechar" un lote reconstruido más tarde como si hubiera sido firmado en el momento
/// original.
/// </summary>
public sealed class AuditBatchSignature
{
    // "|" y no "." como separador de la representación serializable: DateTime.ToString("O") ya usa "."
    // como separador de fracción de segundo (ej. "2026-05-01T12:30:00.0000000Z"), por lo que "." no sirve
    // como delimitador de campo sin ambigüedad.
    private const char FieldSeparator = '|';

    public AuditBatchSignature(string keyVersion, DateTime signedAtUtc, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        KeyVersion = keyVersion;
        SignedAtUtc = DateTime.SpecifyKind(signedAtUtc, DateTimeKind.Utc);
        Value = value;
    }

    /// <summary>
    /// Versión de la clave de firma (<see cref="AuditBatchSigningOptions"/>) usada para producir esta
    /// firma -- resuelta por <see cref="IAuditBatchSigner.VerifyAsync"/> en lugar de la versión activa
    /// vigente, para que rotar la clave activa no invalide firmas ya emitidas (mismo criterio de rotación
    /// que <see cref="Encryption.AesGcmEncryptionProvider"/>, F2-13).
    /// </summary>
    public string KeyVersion { get; }

    /// <summary>Instante UTC en el que se firmó el lote -- forma parte del material firmado, no es solo metadata.</summary>
    public DateTime SignedAtUtc { get; }

    /// <summary>Valor de la firma (HMAC-SHA256, Base64) producido por <see cref="HmacAuditBatchSigner"/>.</summary>
    public string Value { get; }

    /// <summary>
    /// Representación serializable de esta firma (<c>"v{KeyVersion}|{SignedAtUtc:O}|{Value}"</c>), pensada
    /// para que un proyecto consumidor la persista junto con una referencia al lote firmado (por ejemplo, en
    /// el destino WORM de F2-18) -- F2-17 no persiste nada por sí mismo. Ver <see cref="TryParse"/> para la
    /// operación inversa.
    /// </summary>
    public override string ToString() => $"v{KeyVersion}{FieldSeparator}{SignedAtUtc:O}{FieldSeparator}{Value}";

    /// <summary>
    /// Reconstruye una <see cref="AuditBatchSignature"/> a partir de la representación producida por <see
    /// cref="ToString"/>. Devuelve <see langword="false"/> (sin lanzar excepción) ante cualquier formato
    /// inválido -- un dato serializado corrupto o manipulado es un resultado de negocio esperado para quien
    /// lo lea, no una condición excepcional.
    /// </summary>
    public static bool TryParse(string serialized, out AuditBatchSignature? signature)
    {
        signature = null;

        if (string.IsNullOrWhiteSpace(serialized) || serialized[0] != 'v')
        {
            return false;
        }

        var parts = serialized[1..].Split(FieldSeparator, 3);
        if (parts.Length != 3)
        {
            return false;
        }

        var keyVersion = parts[0];
        var signedAtRaw = parts[1];
        var value = parts[2];

        if (string.IsNullOrEmpty(keyVersion) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!DateTime.TryParse(
                signedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var signedAtUtc)
            || signedAtUtc.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        signature = new AuditBatchSignature(keyVersion, signedAtUtc, value);
        return true;
    }
}
