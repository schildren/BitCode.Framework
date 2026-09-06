namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Cache en memoria, de proceso único, del último <see cref="OidcDiscoveryDocument"/> resuelto -- evita
/// una llamada HTTP de descubrimiento por cada login/callback. Se registra como singleton
/// deliberadamente separado de <see cref="OidcDiscoveryDocumentProvider"/> (que es un cliente HTTP
/// tipado, con el ciclo de vida transient estándar de <c>IHttpClientFactory</c>): el estado de cache
/// necesita sobrevivir entre resoluciones transient del cliente. Pública únicamente porque es un
/// parámetro de constructor de <see cref="OidcDiscoveryDocumentProvider"/> (también pública); no está
/// pensada para ser instanciada ni resuelta manualmente por un proyecto consumidor -- se registra y
/// resuelve exclusivamente vía <c>AddSharedOidcAuthorizationCodeFlow</c>.
/// </summary>
public sealed class OidcDiscoveryDocumentCache
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private OidcDiscoveryDocument? _document;
    private string? _metadataAddress;
    private DateTimeOffset _expiresAtUtc;

    public async Task<OidcDiscoveryDocument> GetOrAddAsync(
        string metadataAddress,
        Func<CancellationToken, Task<OidcDiscoveryDocument>> factory,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        if (TryGetFresh(metadataAddress, out var cached))
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFresh(metadataAddress, out cached))
            {
                return cached;
            }

            var document = await factory(cancellationToken);
            _document = document;
            _metadataAddress = metadataAddress;
            _expiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime);
            return document;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool TryGetFresh(string metadataAddress, out OidcDiscoveryDocument document)
    {
        if (_document is not null && _metadataAddress == metadataAddress && _expiresAtUtc > DateTimeOffset.UtcNow)
        {
            document = _document;
            return true;
        }

        document = null!;
        return false;
    }
}
