namespace BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;

/// <summary>
/// Metadata de un objeto ya escrito en <see cref="IWormStorage"/> (F2-18, Épica F2-D). <see
/// cref="ContentHash"/> permite a un llamador verificar la integridad del contenido leído de vuelta sin
/// depender únicamente de que la comparación byte a byte contra el original esté disponible en el mismo
/// proceso (por ejemplo, para un proceso de auditoría/cumplimiento que solo recibió esta metadata).
/// </summary>
public sealed class WormObjectMetadata
{
    public WormObjectMetadata(string key, DateTime writtenAtUtc, DateTime retentionExpiresAtUtc, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        Key = key;
        WrittenAtUtc = DateTime.SpecifyKind(writtenAtUtc, DateTimeKind.Utc);
        RetentionExpiresAtUtc = DateTime.SpecifyKind(retentionExpiresAtUtc, DateTimeKind.Utc);
        ContentHash = contentHash;
    }

    public string Key { get; }

    public DateTime WrittenAtUtc { get; }

    /// <summary>Instante a partir del cual <see cref="IWormStorage.DeleteAsync"/> puede eliminar este objeto.</summary>
    public DateTime RetentionExpiresAtUtc { get; }

    /// <summary>SHA-256 (hex, mayúsculas) del contenido escrito.</summary>
    public string ContentHash { get; }
}
