using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Intercambia un <c>code</c> de autorización por tokens contra el <c>token_endpoint</c> del IdP,
/// aportando el <c>code_verifier</c> de PKCE (RFC 7636 sección 4.5) en vez de un <c>client_secret</c>:
/// esta es la pieza concreta que sustituye una credencial confidencial embebida en el cliente por una
/// prueba de posesión de un solo uso -- exactamente lo que permite que el flujo sea seguro para un
/// cliente público (una SPA, o este backend actuando en su nombre) sin guardar ningún secreto.
/// </summary>
public interface IOidcAuthorizationCodeExchanger
{
    Task<Result<OidcTokenResponse>> ExchangeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default);
}
