namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Datos de correlación que deben sobrevivir, del lado del servidor, entre el redirect a
/// <c>/auth/login</c> y el callback en <c>/auth/callback</c>: <c>State</c> (anti-CSRF), <c>CodeVerifier</c>
/// (PKCE, RFC 7636 -- NUNCA debe viajar al navegador más que dentro de una cookie HttpOnly que la SPA no
/// puede leer), <c>Nonce</c> (anti-replay del id_token, OIDC Core) y <c>ReturnUrl</c> (a dónde volver
/// dentro de la propia aplicación tras el login). Este es exactamente el tipo de dato que
/// <c>IOidcAuthorizationCodeStateProtector</c> cifra/firma antes de guardarlo en la cookie de
/// correlación -- ni la SPA ni ningún intermediario de red pueden leerlo ni falsificarlo.
/// </summary>
public sealed record OidcAuthorizationCodeState(
    string State,
    string CodeVerifier,
    string Nonce,
    string RedirectUri,
    string? ReturnUrl);
