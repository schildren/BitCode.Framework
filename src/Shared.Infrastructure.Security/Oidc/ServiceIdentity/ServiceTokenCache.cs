using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;

/// <summary>
/// Cache en memoria, de proceso único, del último <see cref="ServiceAccessToken"/> obtenido y del
/// <c>token_endpoint</c> resuelto por descubrimiento -- evita pedir un token nuevo en cada llamada
/// saliente (F2-04, criterio de aceptación implícito de reutilizar mientras el token sea válido) y evita
/// una llamada HTTP de descubrimiento por cada solicitud de token. Mismo patrón que
/// <c>OidcDiscoveryDocumentCache</c> (F2-02): se registra como singleton, deliberadamente separado de
/// <see cref="ServiceTokenProvider"/> (cliente HTTP tipado, ciclo de vida transient estándar de
/// <c>IHttpClientFactory</c>) porque el estado de cache necesita sobrevivir entre resoluciones transient.
/// </summary>
public sealed class ServiceTokenCache
{
    // Dos semáforos separados, deliberadamente: obtener un token nuevo (GetOrRefreshAsync) resuelve el
    // token_endpoint como parte de ese mismo flujo (GetOrResolveTokenEndpointAsync) -- si ambos métodos
    // compartieran un único SemaphoreSlim (no reentrante), la segunda adquisición se bloquearía para
    // siempre esperando a que la primera (que la está esperando a ella) se libere.
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);
    private readonly SemaphoreSlim _tokenEndpointRefreshLock = new(1, 1);
    private ServiceAccessToken? _token;
    private string? _tokenEndpoint;
    private DateTimeOffset _tokenEndpointExpiresAtUtc;

    /// <summary>
    /// Devuelve el token vigente si ya se solicitó uno y le queda al menos <paramref name="refreshSkew"/>
    /// de vigencia; en caso contrario invoca <paramref name="fetchNewToken"/> para obtener uno nuevo. Un
    /// resultado fallido de <paramref name="fetchNewToken"/> nunca se cachea -- la siguiente llamada
    /// vuelve a intentar, en vez de recordar un fallo transitorio (p. ej. el IdP momentáneamente caído).
    /// </summary>
    public async Task<Result<ServiceAccessToken>> GetOrRefreshAsync(
        Func<CancellationToken, Task<Result<ServiceAccessToken>>> fetchNewToken,
        TimeSpan refreshSkew,
        CancellationToken cancellationToken)
    {
        if (TryGetValid(refreshSkew, out var cached))
        {
            return Result.Success(cached);
        }

        await _tokenRefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetValid(refreshSkew, out cached))
            {
                return Result.Success(cached);
            }

            var result = await fetchNewToken(cancellationToken);
            if (result.IsSuccess)
            {
                _token = result.Value;
            }

            return result;
        }
        finally
        {
            _tokenRefreshLock.Release();
        }
    }

    /// <summary>
    /// Devuelve el <c>token_endpoint</c> resuelto si todavía está dentro de <paramref name="lifetime"/> de
    /// su última resolución; en caso contrario invoca <paramref name="resolveTokenEndpoint"/> (descubrimiento
    /// OIDC estándar) y cachea el resultado.
    /// </summary>
    public async Task<string> GetOrResolveTokenEndpointAsync(
        Func<CancellationToken, Task<string>> resolveTokenEndpoint,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        if (_tokenEndpoint is not null && _tokenEndpointExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return _tokenEndpoint;
        }

        await _tokenEndpointRefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_tokenEndpoint is not null && _tokenEndpointExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return _tokenEndpoint;
            }

            var endpoint = await resolveTokenEndpoint(cancellationToken);
            _tokenEndpoint = endpoint;
            _tokenEndpointExpiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime);
            return endpoint;
        }
        finally
        {
            _tokenEndpointRefreshLock.Release();
        }
    }

    private bool TryGetValid(TimeSpan refreshSkew, out ServiceAccessToken token)
    {
        if (_token is not null && _token.ExpiresAtUtc - refreshSkew > DateTimeOffset.UtcNow)
        {
            token = _token;
            return true;
        }

        token = null!;
        return false;
    }
}
