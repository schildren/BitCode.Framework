using System.Security.Cryptography;
using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Encryption;

/// <summary>
/// Implementación estándar de <see cref="IEncryptionProvider"/> (F2-13) sobre AES-256-GCM (algoritmo
/// aprobado por la política criptográfica, <c>docs/politica-criptografica.md</c>: cifrado autenticado,
/// detecta alteración del texto cifrado, sin modo de bloque inseguro tipo ECB/CBC sin HMAC). El material
/// de clave no lo administra esta clase: lo resuelve <see cref="ISecretProvider"/> (F2-12) por clave
/// lógica versionada -- <see cref="AesGcmEncryptionProvider"/> nunca guarda ni cachea la clave más allá de
/// la operación en curso.
///
/// Formato del texto cifrado producido: <c>"v{version}.{Base64(nonce(12) || ciphertext || tag(16))}"</c>.
/// La versión va embebida en texto claro (no es secreta -- identifica qué clave usar, no expone la
/// clave en sí) para que <see cref="DecryptAsync"/> pueda resolver la clave correcta sin depender de cuál
/// sea la versión activa en el momento de descifrar. Esto es lo que permite:
/// <list type="bullet">
/// <item>Rotar la clave activa sin downtime: los datos nuevos usan la versión nueva; los datos
/// existentes, cifrados con la versión anterior, se siguen descifrando sin ningún cambio de código
/// mientras esa versión de clave no se elimine del proveedor de secretos.</item>
/// <item>Recuperación: si se pierde o corrompe el puntero de "versión activa" (<see cref="EncryptionOptions.ActiveKeyVersionSecretKey"/>),
/// los datos ya cifrados siguen siendo recuperables en tanto el material de su propia versión de clave
/// archivada siga existiendo en el proveedor de secretos -- la recuperación de datos no depende de la
/// vigencia del puntero de versión activa, solo de la versión embebida en cada texto cifrado.</item>
/// </list>
/// </summary>
public sealed class AesGcmEncryptionProvider : IEncryptionProvider
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32; // AES-256

    private readonly ISecretProvider _secretProvider;
    private readonly IOptions<EncryptionOptions> _options;

    public AesGcmEncryptionProvider(ISecretProvider secretProvider, IOptions<EncryptionOptions> options)
    {
        _secretProvider = secretProvider;
        _options = options;
    }

    public async Task<Result<string>> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
    {
        if (plaintext is null)
        {
            return Result.Failure<string>(Error.Validation(
                "Encryption.InvalidPlaintext", "El texto a cifrar no puede ser null."));
        }

        var activeVersionResult = await _secretProvider.GetSecretAsync(
            _options.Value.ActiveKeyVersionSecretKey, cancellationToken).ConfigureAwait(false);
        if (activeVersionResult.IsFailure)
        {
            return Result.Failure<string>(Error.Failure(
                "Encryption.ActiveKeyVersionNotConfigured",
                $"No se pudo resolver la versión de clave activa ('{_options.Value.ActiveKeyVersionSecretKey}'): {activeVersionResult.Error.Description}"));
        }

        var version = activeVersionResult.Value;
        var keyResult = await ResolveKeyAsync(version, cancellationToken).ConfigureAwait(false);
        if (keyResult.IsFailure)
        {
            return Result.Failure<string>(keyResult.Error);
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertextBytes = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aesGcm = new AesGcm(keyResult.Value, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertextBytes, tag);
        }

        var payload = new byte[nonce.Length + ciphertextBytes.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(ciphertextBytes, 0, payload, nonce.Length, ciphertextBytes.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length + ciphertextBytes.Length, tag.Length);

        return Result.Success($"v{version}.{Convert.ToBase64String(payload)}");
    }

    public async Task<Result<string>> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            return Result.Failure<string>(Error.Validation(
                "Encryption.InvalidCiphertext", "El texto cifrado no puede ser vacío."));
        }

        if (ciphertext[0] != 'v')
        {
            return Result.Failure<string>(Error.Validation(
                "Encryption.InvalidCiphertextFormat",
                "El texto cifrado no tiene el formato esperado 'v{version}.{payload}'."));
        }

        var separatorIndex = ciphertext.IndexOf('.');
        if (separatorIndex <= 1)
        {
            return Result.Failure<string>(Error.Validation(
                "Encryption.InvalidCiphertextFormat",
                "El texto cifrado no tiene el formato esperado 'v{version}.{payload}'."));
        }

        var version = ciphertext[1..separatorIndex];
        var payloadBase64 = ciphertext[(separatorIndex + 1)..];

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(payloadBase64);
        }
        catch (FormatException)
        {
            return Result.Failure<string>(Error.Validation(
                "Encryption.InvalidCiphertextFormat", "El payload cifrado no es Base64 válido."));
        }

        if (payload.Length < NonceSizeBytes + TagSizeBytes)
        {
            return Result.Failure<string>(Error.Validation(
                "Encryption.InvalidCiphertextFormat", "El payload cifrado es demasiado corto."));
        }

        var keyResult = await ResolveKeyAsync(version, cancellationToken).ConfigureAwait(false);
        if (keyResult.IsFailure)
        {
            return Result.Failure<string>(keyResult.Error);
        }

        var nonce = payload[..NonceSizeBytes];
        var tag = payload[^TagSizeBytes..];
        var actualCiphertext = payload[NonceSizeBytes..^TagSizeBytes];
        var plaintextBytes = new byte[actualCiphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(keyResult.Value, TagSizeBytes);
            aesGcm.Decrypt(nonce, actualCiphertext, tag, plaintextBytes);
        }
        catch (CryptographicException)
        {
            return Result.Failure<string>(Error.Failure(
                "Encryption.TamperedOrCorruptedCiphertext",
                "El texto cifrado no pudo descifrarse: fue alterado, corrompido, o cifrado con una clave distinta a la resuelta para su versión."));
        }

        return Result.Success(Encoding.UTF8.GetString(plaintextBytes));
    }

    private async Task<Result<byte[]>> ResolveKeyAsync(string version, CancellationToken cancellationToken)
    {
        var secretKey = $"{_options.Value.KeyMaterialSecretKeyPrefix}{version}";
        var secretResult = await _secretProvider.GetSecretAsync(secretKey, cancellationToken).ConfigureAwait(false);
        if (secretResult.IsFailure)
        {
            return Result.Failure<byte[]>(Error.NotFound(
                "Encryption.KeyNotFound",
                $"No existe material de clave para la versión '{version}' ('{secretKey}'). La versión pudo eliminarse ('crypto shredding' deliberado) o nunca haberse aprovisionado -- ver docs/politica-criptografica.md."));
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(secretResult.Value);
        }
        catch (FormatException)
        {
            return Result.Failure<byte[]>(Error.Failure(
                "Encryption.InvalidKeyMaterial",
                $"El material de clave de la versión '{version}' no es Base64 válido."));
        }

        if (keyBytes.Length != KeySizeBytes)
        {
            return Result.Failure<byte[]>(Error.Failure(
                "Encryption.InvalidKeyMaterial",
                $"El material de clave de la versión '{version}' debe tener {KeySizeBytes} bytes (AES-256); tiene {keyBytes.Length}."));
        }

        return Result.Success(keyBytes);
    }
}
