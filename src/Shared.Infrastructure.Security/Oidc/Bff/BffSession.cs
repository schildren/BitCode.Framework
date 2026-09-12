namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;

/// <summary>
/// Estado server-side de una sesión BFF (F2-03): todo lo que hace falta reconstruir para servir el
/// resto de la vida de la sesión sin volver a pedirle tokens al IdP. El navegador nunca ve ninguno de
/// estos valores directamente -- solo recibe una cookie de sesión propia (HttpOnly/Secure/SameSite)
/// cuyo valor es un identificador opaco (<see cref="IBffSessionStore"/>) que resuelve a esta instancia.
/// </summary>
/// <param name="AccessToken">Access token vigente para llamar a las APIs protegidas en nombre del usuario (nunca expuesto al navegador; lo adjunta el proxy del BFF, ver <c>BffAccessTokenRequestTransform</c>, Shared.Infrastructure.Web).</param>
/// <param name="RefreshToken">Refresh token del IdP, si el flujo lo entregó. Reservado para renovación de sesión (F2-06); F2-03 solo lo persiste server-side, no lo usa todavía.</param>
/// <param name="IdToken">Id token original, conservado únicamente para poder iniciar un RP-initiated logout contra el IdP (<c>end_session_endpoint</c>) al cerrar sesión.</param>
/// <param name="AccessTokenExpiresAtUtc">Vigencia informada por el IdP para <see cref="AccessToken"/>, si el token endpoint la reportó.</param>
/// <param name="Claims">Subconjunto mínimo de claims del id_token (p. ej. "sub", "name", "email", "preferred_username") necesario para reconstruir el <c>ClaimsPrincipal</c> de la sesión sin volver a decodificar el id_token en cada request.</param>
public sealed record BffSession(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    IReadOnlyDictionary<string, string> Claims);
