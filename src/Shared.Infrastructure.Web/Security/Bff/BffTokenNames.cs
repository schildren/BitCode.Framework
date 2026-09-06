namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Nombres usados para guardar los tokens del IdP dentro de <c>AuthenticationProperties</c>
/// (<c>AuthenticationProperties.StoreTokens</c>/<c>HttpContext.GetTokenAsync</c>) -- el mismo mecanismo
/// estándar que usa el propio middleware OpenIdConnect de ASP.NET Core cuando <c>SaveTokens = true</c>,
/// reutilizado aquí para no inventar un canal paralelo de acceso a los tokens de la sesión.
/// </summary>
internal static class BffTokenNames
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";
    public const string IdToken = "id_token";
    public const string ExpiresAt = "expires_at";
}
