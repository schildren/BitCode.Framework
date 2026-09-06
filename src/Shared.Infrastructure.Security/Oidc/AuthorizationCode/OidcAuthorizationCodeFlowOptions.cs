namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Opciones propias del flujo Authorization Code + PKCE (F2-02), separadas de <see cref="OidcOptions"/>
/// (que describe la conexión con el IdP para validar tokens, F2-01): estas describen cómo ESTE backend
/// participa como cliente OAuth2/OIDC público al iniciar sesión un usuario web.
/// </summary>
public sealed class OidcAuthorizationCodeFlowOptions
{
    public const string SectionName = "Oidc:AuthorizationCode";

    /// <summary>
    /// URI de callback absoluta registrada en el cliente del IdP (p. ej.
    /// "https://api.bitcode.local/auth/callback"). Debe coincidir exactamente (RFC 6749 sección 3.1.2)
    /// con la que procesa <c>MapSharedOidcAuthorizationCodeLogin</c> (Shared.Infrastructure.Web) y con
    /// la registrada del lado del IdP -- un mismatch es la causa más común de "invalid_grant" al
    /// intercambiar el code.
    /// </summary>
    public required string RedirectUri { get; set; }

    /// <summary>
    /// Scopes solicitados en la request de autorización. "openid" es obligatorio para que la respuesta
    /// del token endpoint incluya un id_token (OIDC Core sección 3.1.2.1).
    /// </summary>
    public string Scope { get; set; } = "openid profile email";

    /// <summary>
    /// Vigencia de la cookie de correlación HttpOnly que guarda <c>state</c>/<c>code_verifier</c>/<c>nonce</c>
    /// entre el redirect a <c>/auth/login</c> y el callback en <c>/auth/callback</c>. Debe alcanzar para
    /// que el usuario complete el login interactivo en el IdP (credenciales, MFA); 10 minutos por
    /// defecto es el mismo orden de magnitud que usa el middleware OpenIdConnect estándar de ASP.NET
    /// Core para su propia cookie de correlación.
    /// </summary>
    public TimeSpan CorrelationCookieLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// URI absoluta a la que el IdP redirige tras un RP-Initiated Logout exitoso (parámetro estándar
    /// <c>post_logout_redirect_uri</c>). Opcional -- solo hace falta si el proyecto consumidor usa
    /// <c>MapSharedBffLogout</c> (F2-03) y el IdP publica <c>end_session_endpoint</c>; sin configurar,
    /// el logout sigue cerrando la sesión propia del BFF, simplemente no propaga el cierre de sesión SSO
    /// al IdP.
    /// </summary>
    public string? PostLogoutRedirectUri { get; set; }
}
