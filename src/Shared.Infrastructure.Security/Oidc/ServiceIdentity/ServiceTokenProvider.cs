using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;

/// <summary>
/// Implementación estándar de <see cref="IServiceTokenProvider"/>: <c>POST</c>
/// <c>application/x-www-form-urlencoded</c> al <c>token_endpoint</c> descubierto con
/// <c>grant_type=client_credentials</c> y <c>client_secret</c> (RFC 6749 sección 4.4) -- sin usuario
/// humano ni redirección involucrados, a diferencia de <c>OidcAuthorizationCodeExchanger</c> (F2-02). El
/// resultado se cachea en <see cref="ServiceTokenCache"/> y se renueva automáticamente antes de expirar
/// (con un margen de <see cref="TokenRefreshSkew"/>).
/// </summary>
public sealed class ServiceTokenProvider : IServiceTokenProvider
{
    private static readonly TimeSpan TokenEndpointCacheLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Margen de seguridad antes de la expiración real del token en el que se lo considera "por vencer"
    /// y se solicita uno nuevo -- evita que una llamada saliente use un token que expira a mitad de la
    /// petición HTTP o que el IdP rechace por un desfasaje de reloj mínimo entre procesos.
    /// </summary>
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Vigencia asumida cuando el IdP no informa <c>expires_in</c> (infrecuente, pero no todo proveedor
    /// conforme a OAuth2 lo hace obligatorio) -- deliberadamente conservadora para forzar una renovación
    /// frecuente en vez de asumir una vigencia larga sin evidencia.
    /// </summary>
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IOptions<ServiceIdentityOptions> _options;
    private readonly ServiceTokenCache _cache;

    public ServiceTokenProvider(HttpClient httpClient, IOptions<ServiceIdentityOptions> options, ServiceTokenCache cache)
    {
        _httpClient = httpClient;
        _options = options;
        _cache = cache;
    }

    public Task<Result<ServiceAccessToken>> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrRefreshAsync(FetchNewTokenAsync, TokenRefreshSkew, cancellationToken);

    private async Task<Result<ServiceAccessToken>> FetchNewTokenAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;

        if (!string.IsNullOrWhiteSpace(options.CertificateThumbprint))
        {
            // Contrato preparado (ServiceIdentityOptions.CertificateThumbprint) para autenticación de
            // cliente por certificado (mTLS/private_key_jwt) o workload identity federada, pero el flujo
            // concreto todavía no está implementado en F2-04 -- ver docs/guia-oidc-adapter.md, sección
            // "F2-04", "queda fuera de alcance". Falla explícitamente en vez de intentar un client_secret
            // vacío o ignorar el thumbprint en silencio.
            return Result.Failure<ServiceAccessToken>(Error.Failure(
                "ServiceIdentity.CertificateAuthenticationNotSupported",
                "La autenticación de identidad de servicio por certificado (CertificateThumbprint) todavía no está implementada; configure ClientSecret."));
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return Result.Failure<ServiceAccessToken>(Error.Failure(
                "ServiceIdentity.MissingCredential",
                $"'{ServiceIdentityOptions.SectionName}:{nameof(ServiceIdentityOptions.ClientSecret)}' o '{nameof(ServiceIdentityOptions.CertificateThumbprint)}' es obligatorio para obtener un token de identidad de servicio."));
        }

        string tokenEndpoint;
        try
        {
            tokenEndpoint = await _cache.GetOrResolveTokenEndpointAsync(
                ct => ResolveTokenEndpointAsync(options, ct),
                TokenEndpointCacheLifetime,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return Result.Failure<ServiceAccessToken>(Error.Failure(
                "ServiceIdentity.DiscoveryFailed",
                $"No se pudo resolver el 'token_endpoint' del IdP: {ex.Message}"));
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
        };

        if (!string.IsNullOrWhiteSpace(options.Scope))
        {
            form["scope"] = options.Scope;
        }

        using var requestContent = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(tokenEndpoint, requestContent, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorPayload = TryDeserialize<TokenErrorPayload>(body);
            // "invalid_client" es la respuesta estándar del IdP para client_id/client_secret incorrectos
            // (RFC 6749 sección 5.2) -- el fallo de credenciales pedido explícitamente por el criterio de
            // prueba de F2-04 llega por este camino.
            var errorCode = errorPayload?.Error ?? "unknown_error";
            var description = errorPayload?.ErrorDescription ?? (string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);
            return Result.Failure<ServiceAccessToken>(
                Error.Unauthorized($"ServiceIdentity.TokenRequest.{errorCode}", description ?? "El IdP rechazó la solicitud de token de identidad de servicio."));
        }

        var payload = TryDeserialize<TokenSuccessPayload>(body);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            return Result.Failure<ServiceAccessToken>(
                Error.Failure("ServiceIdentity.TokenRequest.InvalidResponse", "El token endpoint respondió 2xx sin un access_token utilizable."));
        }

        var lifetime = payload.ExpiresIn.HasValue && payload.ExpiresIn.Value > 0
            ? TimeSpan.FromSeconds(payload.ExpiresIn.Value)
            : DefaultTokenLifetime;

        return Result.Success(new ServiceAccessToken(
            payload.AccessToken,
            payload.TokenType ?? "Bearer",
            DateTimeOffset.UtcNow.Add(lifetime)));
    }

    private async Task<string> ResolveTokenEndpointAsync(ServiceIdentityOptions options, CancellationToken cancellationToken)
    {
        var metadataAddress = string.IsNullOrWhiteSpace(options.MetadataAddress)
            ? $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration"
            : options.MetadataAddress;

        using var response = await _httpClient.GetAsync(metadataAddress, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DiscoveryPayload>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                $"El documento de metadata OIDC en '{metadataAddress}' no se pudo interpretar como JSON.");

        if (string.IsNullOrWhiteSpace(payload.TokenEndpoint))
        {
            throw new InvalidOperationException(
                $"El documento de metadata OIDC en '{metadataAddress}' no declara 'token_endpoint'.");
        }

        return payload.TokenEndpoint;
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
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);

    private sealed record TokenErrorPayload(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record DiscoveryPayload(
        [property: JsonPropertyName("token_endpoint")] string? TokenEndpoint);
}
