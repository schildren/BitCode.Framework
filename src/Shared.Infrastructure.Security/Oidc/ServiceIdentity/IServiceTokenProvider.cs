using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;

/// <summary>
/// Obtiene (y reutiliza mientras sea válido) el token de acceso de la identidad propia de este workload
/// contra el <c>token_endpoint</c> del IdP (OAuth2 Client Credentials, RFC 6749 sección 4.4) -- sin
/// usuario humano involucrado. Cada workload que llama a otra API del framework en nombre propio resuelve
/// su token a través de esta abstracción en vez de manejar el intercambio o el cacheo a mano; ver
/// <see cref="ServiceIdentityAuthenticationHandler"/> para el punto de consumo estándar sobre un
/// <see cref="System.Net.Http.HttpClient"/> saliente.
/// </summary>
public interface IServiceTokenProvider
{
    Task<Result<ServiceAccessToken>> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
