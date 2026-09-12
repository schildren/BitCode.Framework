using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Implementación de <see cref="IOidcDiscoveryDocumentProvider"/> que descarga
/// "{Authority}/.well-known/openid-configuration" (o <see cref="OidcOptions.MetadataAddress"/> si está
/// configurado) vía un <see cref="HttpClient"/> tipado registrado con la pipeline de resiliencia
/// estándar del framework (F1-26, <c>AddResilientHttpClient</c>) y cachea el resultado 24hs en
/// <see cref="OidcDiscoveryDocumentCache"/>.
/// </summary>
public sealed class OidcDiscoveryDocumentProvider : IOidcDiscoveryDocumentProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly IOptions<OidcOptions> _oidcOptions;
    private readonly OidcDiscoveryDocumentCache _cache;

    public OidcDiscoveryDocumentProvider(HttpClient httpClient, IOptions<OidcOptions> oidcOptions, OidcDiscoveryDocumentCache cache)
    {
        _httpClient = httpClient;
        _oidcOptions = oidcOptions;
        _cache = cache;
    }

    public Task<OidcDiscoveryDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        var oidc = _oidcOptions.Value;
        var metadataAddress = string.IsNullOrWhiteSpace(oidc.MetadataAddress)
            ? $"{oidc.Authority.TrimEnd('/')}/.well-known/openid-configuration"
            : oidc.MetadataAddress;

        return _cache.GetOrAddAsync(metadataAddress, ct => FetchAsync(metadataAddress, ct), CacheLifetime, cancellationToken);
    }

    private async Task<OidcDiscoveryDocument> FetchAsync(string metadataAddress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(metadataAddress, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DiscoveryPayload>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                $"El documento de metadata OIDC en '{metadataAddress}' no se pudo interpretar como JSON.");

        if (string.IsNullOrWhiteSpace(payload.AuthorizationEndpoint) || string.IsNullOrWhiteSpace(payload.TokenEndpoint))
        {
            throw new InvalidOperationException(
                $"El documento de metadata OIDC en '{metadataAddress}' no declara 'authorization_endpoint'/'token_endpoint'.");
        }

        return new OidcDiscoveryDocument(payload.AuthorizationEndpoint, payload.TokenEndpoint, payload.EndSessionEndpoint);
    }

    private sealed record DiscoveryPayload(
        [property: JsonPropertyName("authorization_endpoint")] string? AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] string? TokenEndpoint,
        [property: JsonPropertyName("end_session_endpoint")] string? EndSessionEndpoint);
}
