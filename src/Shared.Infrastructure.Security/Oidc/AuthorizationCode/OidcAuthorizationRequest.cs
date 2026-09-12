namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Resultado de construir una request de autorización: la URL absoluta a la que redirigir al usuario
/// (endpoint de autorización del IdP + parámetros OAuth2/OIDC) y el <see cref="OidcAuthorizationCodeState"/>
/// que el llamador debe persistir server-side (cookie de correlación) hasta el callback.
/// </summary>
public sealed record OidcAuthorizationRequest(Uri AuthorizationUri, OidcAuthorizationCodeState State);

/// <summary>
/// Construye la request de autorización de un flujo Authorization Code + PKCE (F2-02): siempre
/// <c>response_type=code</c> -- nunca <c>token</c> ni <c>id_token</c> (criterio de aceptación "Sin flujo
/// implícito"), con <c>code_challenge</c>/<c>code_challenge_method=S256</c> (RFC 7636) y <c>state</c>/
/// <c>nonce</c> de un solo uso.
/// </summary>
public interface IOidcAuthorizationRequestFactory
{
    Task<OidcAuthorizationRequest> CreateAsync(string? returnUrl, CancellationToken cancellationToken = default);
}
