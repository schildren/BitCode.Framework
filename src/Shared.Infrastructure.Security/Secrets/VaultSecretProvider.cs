using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Secrets;

/// <summary>
/// Proveedor de secretos sobre HashiCorp Vault (motor KV versión 2), consumido vía su API HTTP estándar
/// (<c>GET {Address}/v1/{MountPath}/data/{PathPrefix}/{key}</c>, header <c>X-Vault-Token</c>) — sin un
/// SDK cliente de terceros, mismo estilo minimalista que <c>ServiceTokenProvider</c> (F2-04) y
/// <c>OidcAuthorizationCodeExchanger</c> (F2-02), que tampoco usan un SDK propietario del IdP.
/// <see cref="VaultSecretProviderOptions.Address"/>/<see cref="VaultSecretProviderOptions.Token"/> son
/// intercambiables por configuración -- este tipo nunca se referencia desde código de negocio, solo desde
/// <see cref="SecretProviderServiceCollectionExtensions"/> (mismo principio que el adapter OIDC, ADR 0004).
///
/// <para>
/// Estado del proveedor: ADR 0014 lo deja como <c>Proposed</c> (no <c>Accepted</c>) porque la elección de
/// proveedor de secretos/KMS requiere aprobación humana explícita (Plan Maestro sección 13) antes de
/// habilitarse contra un Vault productivo real -- esta implementación es la abstracción y el adapter
/// concreto que F2-12 exige como entregable ("Abstracción y provider"), verificado con Testcontainers,
/// pero no implica que ya exista una decisión de adopción productiva.
/// </para>
/// </summary>
public sealed class VaultSecretProvider : ISecretProvider
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<VaultSecretProviderOptions> _options;

    public VaultSecretProvider(HttpClient httpClient, IOptions<VaultSecretProviderOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<Result<string>> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<string>(
                Error.Validation("Secrets.InvalidKey", "La clave del secreto no puede ser vacía."));
        }

        var options = _options.Value;
        var relativePath = BuildDataPath(options, key);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Add("X-Vault-Token", options.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(Error.Failure(
                "Secrets.ProviderUnavailable",
                $"No se pudo contactar a Vault en '{options.Address}': {ex.Message}"));
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Failure<string>(Error.NotFound(
                    "Secrets.NotFound",
                    $"No existe un secreto con clave '{key}' en Vault ('{relativePath}')."));
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                return Result.Failure<string>(Error.Unauthorized(
                    "Secrets.AccessDenied",
                    $"Vault rechazó el token configurado para leer '{relativePath}' ({(int)response.StatusCode})."));
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<string>(Error.Failure(
                    "Secrets.ProviderUnavailable",
                    $"Vault respondió {(int)response.StatusCode} al leer '{relativePath}': {body}"));
            }

            VaultKvV2ReadResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<VaultKvV2ReadResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException ex)
            {
                return Result.Failure<string>(Error.Failure(
                    "Secrets.ProviderUnavailable",
                    $"La respuesta de Vault para '{relativePath}' no se pudo interpretar como JSON: {ex.Message}"));
            }

            var value = payload?.Data?.Data is { } fields && fields.TryGetValue(options.ValueFieldName, out var fieldValue)
                ? fieldValue.GetString()
                : null;

            return string.IsNullOrEmpty(value)
                ? Result.Failure<string>(Error.NotFound(
                    "Secrets.NotFound",
                    $"El secreto '{key}' existe en Vault pero no tiene el campo '{options.ValueFieldName}'."))
                : Result.Success(value);
        }
    }

    /// <summary>
    /// Construye la ruta relativa de lectura de la API KV v2 de Vault:
    /// <c>v1/{MountPath}/data/{PathPrefix}/{key}</c> (el segmento fijo <c>"data"</c> es parte del
    /// contrato KV v2 de Vault, no configurable -- distingue la ruta de lectura de datos de la ruta de
    /// metadata, <c>v1/{MountPath}/metadata/...</c>, que este provider no usa).
    /// </summary>
    private static string BuildDataPath(VaultSecretProviderOptions options, string key)
    {
        var mount = options.MountPath.Trim('/');
        var prefix = options.PathPrefix.Trim('/');
        var segments = string.IsNullOrEmpty(prefix) ? key : $"{prefix}/{key}";
        return $"v1/{mount}/data/{segments}";
    }

    private sealed record VaultKvV2ReadResponse(
        [property: JsonPropertyName("data")] VaultKvV2ReadData? Data);

    private sealed record VaultKvV2ReadData(
        [property: JsonPropertyName("data")] Dictionary<string, JsonElement>? Data);
}
