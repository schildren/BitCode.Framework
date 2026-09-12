using System.Text.Json.Serialization;
using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Implementación estándar de <see cref="IOidcAuthorizationCodeExchanger"/>: <c>POST</c>
/// <c>application/x-www-form-urlencoded</c> al <c>token_endpoint</c> descubierto
/// (<see cref="IOidcDiscoveryDocumentProvider"/>) con <c>grant_type=authorization_code</c> y
/// <c>code_verifier</c> -- deliberadamente SIN <c>client_secret</c> (cliente público protegido por PKCE,
/// RFC 7636). Ningún caller de este framework necesita, ni debe, guardar una credencial confidencial
/// para completar el intercambio.
/// </summary>
public sealed class OidcAuthorizationCodeExchanger : IOidcAuthorizationCodeExchanger
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<OidcOptions> _oidcOptions;
    private readonly IOidcDiscoveryDocumentProvider _discoveryDocumentProvider;

    public OidcAuthorizationCodeExchanger(
        HttpClient httpClient,
        IOptions<OidcOptions> oidcOptions,
        IOidcDiscoveryDocumentProvider discoveryDocumentProvider)
    {
        _httpClient = httpClient;
        _oidcOptions = oidcOptions;
        _discoveryDocumentProvider = discoveryDocumentProvider;
    }

    public async Task<Result<OidcTokenResponse>> ExchangeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var discoveryDocument = await _discoveryDocumentProvider.GetAsync(cancellationToken);
        var oidc = _oidcOptions.Value;

        using var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = oidc.ClientId ?? string.Empty,
            ["code_verifier"] = codeVerifier,
        });

        using var response = await _httpClient.PostAsync(discoveryDocument.TokenEndpoint, requestContent, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorPayload = TryDeserialize<TokenErrorPayload>(body);
            // "invalid_grant" es la respuesta estándar del IdP tanto para un code inválido/expirado
            // como para un code_verifier que no matchea el code_challenge original (PKCE mismatch,
            // RFC 7636 sección 4.6) -- el IdP, no este código, es quien valida esa correspondencia
            // criptográfica, así que ambos casos llegan aquí de la misma forma.
            var errorCode = errorPayload?.Error ?? "unknown_error";
            var description = errorPayload?.ErrorDescription ?? (string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);
            return Result.Failure<OidcTokenResponse>(
                Error.Unauthorized($"Oidc.TokenExchange.{errorCode}", description ?? "El IdP rechazó el intercambio de code."));
        }

        var payload = TryDeserialize<TokenSuccessPayload>(body);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            return Result.Failure<OidcTokenResponse>(
                Error.Failure("Oidc.TokenExchange.InvalidResponse", "El token endpoint respondió 2xx sin un access_token utilizable."));
        }

        return Result.Success(new OidcTokenResponse(
            payload.AccessToken,
            payload.IdToken,
            payload.RefreshToken,
            payload.TokenType ?? "Bearer",
            payload.ExpiresIn));
    }

    private static T? TryDeserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    private sealed record TokenSuccessPayload(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);

    private sealed record TokenErrorPayload(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
