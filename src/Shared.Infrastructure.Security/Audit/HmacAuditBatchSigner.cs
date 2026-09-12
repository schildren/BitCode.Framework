using System.Security.Cryptography;
using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Implementación por defecto de <see cref="IAuditBatchSigner"/> (F2-17, Épica F2-D) sobre HMAC-SHA256. El
/// material de clave no lo administra esta clase: lo resuelve <see cref="ISecretProvider"/> (F2-12) por
/// clave lógica versionada -- mismo patrón que <see cref="Encryption.AesGcmEncryptionProvider"/> (F2-13),
/// reutilizado deliberadamente en lugar de introducir una gestión de claves paralela para F2-17.
/// <para>
/// <b>Por qué HMAC-SHA256 (simétrico) y no una firma asimétrica</b>: el framework hoy no expone ninguna
/// abstracción de par de claves asimétrico/PKI (solo <see cref="ISecretProvider"/> para material simétrico
/// y <see cref="Encryption.IEncryptionProvider"/>, también simétrico, AES-256-GCM); agregar soporte de
/// claves asimétricas sería introducir una pieza de infraestructura criptográfica nueva no pedida por el
/// entregable de F2-17 ("Integrar firma de lotes o eventos", "Mecanismo aprobado") y fuera del alcance
/// mínimo de esta tarea. HMAC-SHA256 es un algoritmo aprobado por <c>docs/politica-criptografica.md</c>
/// (autenticado, sin modo de bloque inseguro) y cierra exactamente el hueco documentado de F2-16 -- un
/// atacante con acceso de ESCRITURA al almacenamiento de auditoría (pero sin acceso al proveedor de
/// secretos donde vive la clave de firma) no puede reconstruir una cadena alternativa que verifique.
/// </para>
/// <para>
/// <b>"Verificación independiente" con un esquema simétrico</b>: el criterio de aceptación de F2-17 no exige
/// que la verificación pueda hacerse sin ningún secreto (eso sería exclusivo de una firma asimétrica con
/// clave pública) -- exige que la verificación sea independiente de quien firmó. Con HMAC, esto se sostiene
/// mientras firmante y verificador sean procesos/servicios distintos que comparten acceso al mismo
/// perímetro de confianza (el proveedor de secretos), pero NINGUNO de los dos es, por sí solo, quien tiene
/// además acceso de escritura directa al almacenamiento de auditoría subyacente -- por ejemplo, un servicio
/// de auditoría que firma lotes al escribirlos y un proceso de verificación periódico/de cumplimiento que
/// solo lee del almacenamiento y del proveedor de secretos, sin poder escribir en ninguno de los dos. Si el
/// caso de uso de un proyecto consumidor requiriera verificación por un tercero SIN acceso al proveedor de
/// secretos (por ejemplo, un auditor externo), ese proyecto necesita una firma asimétrica -- fuera de
/// alcance de esta tarea, ver "Qué NO resuelve F2-17".
/// </para>
/// </summary>
public sealed class HmacAuditBatchSigner : IAuditBatchSigner
{
    private const int MinimumKeySizeBytes = 32;
    private const char FieldSeparator = ''; // Unit Separator -- mismo criterio que AuditHashCalculator.
    private const string NullPlaceholder = ""; // Group Separator.

    private readonly ISecretProvider _secretProvider;
    private readonly IOptions<AuditBatchSigningOptions> _options;
    private readonly TimeProvider _timeProvider;

    public HmacAuditBatchSigner(
        ISecretProvider secretProvider, IOptions<AuditBatchSigningOptions> options, TimeProvider? timeProvider = null)
    {
        _secretProvider = secretProvider;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<AuditBatchSignature>> SignAsync(
        IReadOnlyList<AuditEntry> batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return Result.Failure<AuditBatchSignature>(Error.Validation(
                "AuditBatchSigning.EmptyBatch", "No se puede firmar un lote de auditoría vacío."));
        }

        var activeVersionResult = await _secretProvider.GetSecretAsync(
            _options.Value.ActiveKeyVersionSecretKey, cancellationToken).ConfigureAwait(false);
        if (activeVersionResult.IsFailure)
        {
            return Result.Failure<AuditBatchSignature>(Error.Failure(
                "AuditBatchSigning.ActiveKeyVersionNotConfigured",
                $"No se pudo resolver la versión de clave de firma activa ('{_options.Value.ActiveKeyVersionSecretKey}'): {activeVersionResult.Error.Description}"));
        }

        var keyVersion = activeVersionResult.Value;
        var keyResult = await ResolveKeyAsync(keyVersion, cancellationToken).ConfigureAwait(false);
        if (keyResult.IsFailure)
        {
            return Result.Failure<AuditBatchSignature>(keyResult.Error);
        }

        var signedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var signatureValue = ComputeSignatureValue(batch, keyVersion, signedAtUtc, keyResult.Value);

        return Result.Success(new AuditBatchSignature(keyVersion, signedAtUtc, signatureValue));
    }

    public async Task<Result<bool>> VerifyAsync(
        IReadOnlyList<AuditEntry> batch, AuditBatchSignature signature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(signature);

        if (batch.Count == 0)
        {
            return Result.Failure<bool>(Error.Validation(
                "AuditBatchSigning.EmptyBatch", "No se puede verificar la firma de un lote de auditoría vacío."));
        }

        var keyResult = await ResolveKeyAsync(signature.KeyVersion, cancellationToken).ConfigureAwait(false);
        if (keyResult.IsFailure)
        {
            return Result.Failure<bool>(keyResult.Error);
        }

        var expectedSignatureValue = ComputeSignatureValue(batch, signature.KeyVersion, signature.SignedAtUtc, keyResult.Value);

        byte[] expectedBytes;
        byte[] actualBytes;
        try
        {
            expectedBytes = Convert.FromBase64String(expectedSignatureValue);
            actualBytes = Convert.FromBase64String(signature.Value);
        }
        catch (FormatException)
        {
            // El valor de la firma provista no es Base64 válido -- no puede coincidir con ninguna firma
            // real calculada por este mismo tipo. Es una firma inválida (Result.Success(false)), no una
            // condición que impida intentar la verificación.
            return Result.Success(false);
        }

        // Comparación en tiempo constante -- evita filtrar, a través del tiempo de respuesta, en qué byte
        // difiere una firma manipulada de la firma esperada (mismo criterio que cualquier comparación de
        // material de autenticación, ej. tags de AES-GCM resueltos internamente por el runtime).
        var isValid = expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);

        return Result.Success(isValid);
    }

    private async Task<Result<byte[]>> ResolveKeyAsync(string keyVersion, CancellationToken cancellationToken)
    {
        var secretKey = $"{_options.Value.KeyMaterialSecretKeyPrefix}{keyVersion}";
        var secretResult = await _secretProvider.GetSecretAsync(secretKey, cancellationToken).ConfigureAwait(false);
        if (secretResult.IsFailure)
        {
            return Result.Failure<byte[]>(Error.NotFound(
                "AuditBatchSigning.KeyNotFound",
                $"No existe material de clave de firma para la versión '{keyVersion}' ('{secretKey}')."));
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(secretResult.Value);
        }
        catch (FormatException)
        {
            return Result.Failure<byte[]>(Error.Failure(
                "AuditBatchSigning.InvalidKeyMaterial",
                $"El material de clave de firma de la versión '{keyVersion}' no es Base64 válido."));
        }

        if (keyBytes.Length < MinimumKeySizeBytes)
        {
            return Result.Failure<byte[]>(Error.Failure(
                "AuditBatchSigning.InvalidKeyMaterial",
                $"El material de clave de firma de la versión '{keyVersion}' debe tener al menos {MinimumKeySizeBytes} bytes; tiene {keyBytes.Length}."));
        }

        return Result.Success(keyBytes);
    }

    /// <summary>
    /// Construye la representación canónica del lote (identidad + hash de contenido RECALCULADO + estado de
    /// cadena de cada registro, en el orden dado) junto con el instante de firma y la versión de clave, y
    /// calcula HMAC-SHA256 sobre ella.
    /// <para>
    /// Deliberadamente RECALCULA el hash de contenido de cada registro con <see
    /// cref="AuditHashCalculator.Compute"/> sobre sus campos actuales, en lugar de confiar en el valor ya
    /// almacenado en <see cref="AuditEntry.AuditHash"/> -- son dos cosas distintas y la diferencia importa:
    /// <see cref="AuditEntry.AuditHash"/> es un campo más del registro, que un atacante con acceso de
    /// escritura directa al almacenamiento podría alterar (o dejar sin actualizar) junto con cualquier otro
    /// campo. Recalcularlo acá, sobre el contenido que efectivamente se está firmando/verificando, es lo que
    /// garantiza que alterar CUALQUIER campo de CUALQUIER registro del lote invalida la firma, incluso en el
    /// caso límite de que el atacante modifique un campo (ej. <see cref="AuditResource"/>) sin también
    /// recalcular y reescribir el <see cref="AuditEntry.AuditHash"/> almacenado de ese registro -- un
    /// registro internamente inconsistente que, sin este recálculo, este firmante no detectaría por sí
    /// solo (aunque sí lo detectaría <see cref="IAuditIntegrityVerifier"/>, F2-16, de forma independiente).
    /// No se recalcula <see cref="AuditEntry.PreviousAuditHash"/> (no es derivable de un único registro,
    /// depende del hash del registro anterior de la cadena) -- se firma tal como está, lo que igual detecta
    /// eliminación/reordenamiento del lote (ver <see cref="AuditIntegrityVerifier"/>, mismo principio).
    /// </para>
    /// </summary>
    private static string ComputeSignatureValue(
        IReadOnlyList<AuditEntry> batch, string keyVersion, DateTime signedAtUtc, byte[] key)
    {
        var builder = new StringBuilder();

        AppendField(builder, keyVersion);
        AppendField(builder, signedAtUtc.ToString("O"));
        AppendField(builder, batch.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var entry in batch)
        {
            var contentHash = AuditHashCalculator.Compute(
                entry.Id, entry.OccurredAtUtc, entry.Actor, entry.TenantId, entry.Action, entry.Resource,
                entry.Outcome, entry.Reason, entry.CorrelationId, entry.TraceId, entry.IpAddress, entry.Metadata);

            AppendField(builder, entry.Id.ToString("D"));
            AppendField(builder, contentHash);
            AppendField(builder, entry.PreviousAuditHash ?? NullPlaceholder);
        }

        using var hmac = new HMACSHA256(key);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToBase64String(hashBytes);
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        builder.Append(value).Append(FieldSeparator);
    }
}
