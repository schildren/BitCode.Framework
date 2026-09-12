using System.Security.Cryptography;
using System.Text;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Genera y valida los artefactos criptográficos de PKCE (RFC 7636) que F2-02 exige para
/// Authorization Code + PKCE: <c>code_verifier</c>, <c>code_challenge</c> (método <c>S256</c>, el
/// único soportado -- RFC 7636 sección 4.2 desaconseja "plain" salvo que el cliente no pueda hacer
/// SHA-256, lo cual no aplica a un backend .NET) y los valores de correlación <c>state</c>/<c>nonce</c>
/// que impiden CSRF y replay del flujo de autorización.
/// </summary>
public static class PkceGenerator
{
    // RFC 7636 sección 4.1: el code_verifier es una cadena de 43 a 128 caracteres del alfabeto
    // unreserved de RFC 3986 (ALPHA / DIGIT / "-" / "." / "_" / "~").
    private const string UnreservedCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    private const int DefaultCodeVerifierLength = 64;
    private const int DefaultCorrelationTokenByteLength = 32;

    /// <summary>
    /// Genera un <c>code_verifier</c> criptográficamente aleatorio (RFC 7636 sección 4.1).
    /// </summary>
    /// <param name="length">
    /// Longitud en caracteres, entre 43 y 128 (límites de la spec). 64 por defecto: suficiente entropía
    /// sin acercarse al límite superior.
    /// </param>
    public static string GenerateCodeVerifier(int length = DefaultCodeVerifierLength)
    {
        if (length is < 43 or > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                "RFC 7636 exige que el code_verifier tenga entre 43 y 128 caracteres.");
        }

        var randomBytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = UnreservedCharacters[randomBytes[i] % UnreservedCharacters.Length];
        }

        return new string(chars);
    }

    /// <summary>
    /// Calcula el <c>code_challenge</c> método <c>S256</c> (RFC 7636 sección 4.2) a partir de un
    /// <c>code_verifier</c>: <c>BASE64URL-ENCODE(SHA256(ASCII(code_verifier)))</c>, sin padding.
    /// </summary>
    public static string CreateCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);

        var verifierBytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(verifierBytes);
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Genera un token de correlación aleatorio de alta entropía apto para <c>state</c> o <c>nonce</c>
    /// (OAuth2 RFC 6749 sección 10.12 / OIDC Core sección 15.5.2): protege el flujo contra CSRF y
    /// replay, respectivamente. No es un secreto de aplicación -- es de un solo uso, vive el tiempo del
    /// flujo de login y se descarta después del callback.
    /// </summary>
    public static string GenerateCorrelationToken(int byteLength = DefaultCorrelationTokenByteLength)
    {
        if (byteLength < 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength),
                byteLength,
                "Un token de correlación (state/nonce) necesita al menos 16 bytes de entropía.");
        }

        return Base64UrlEncode(RandomNumberGenerator.GetBytes(byteLength));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
