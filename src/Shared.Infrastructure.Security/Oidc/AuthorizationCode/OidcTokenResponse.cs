namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Respuesta exitosa del token endpoint tras intercambiar un <c>code</c> de autorización por tokens
/// (RFC 6749 sección 4.1.4 / OIDC Core sección 3.1.3.3). Este tipo es el "cimiento" que reutilizará
/// F2-03 (BFF): quien invoque <c>IOidcAuthorizationCodeExchanger</c> decide qué hacer con estos tokens
/// (F2-02 no impone establecer una sesión ni exponerlos al navegador -- ver
/// <c>MapSharedOidcAuthorizationCodeLogin</c>, Shared.Infrastructure.Web).
/// </summary>
public sealed record OidcTokenResponse(
    string AccessToken,
    string? IdToken,
    string? RefreshToken,
    string TokenType,
    int? ExpiresInSeconds);
