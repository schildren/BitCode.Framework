namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;

/// <summary>
/// Opciones de la sesión server-side del BFF (F2-03), sección de configuración <c>"Bff:Session"</c>.
/// Separadas de <see cref="OidcAuthorizationCode.OidcAuthorizationCodeFlowOptions"/> (F2-02): esas
/// describen el flujo de login contra el IdP; estas describen la sesión propia que el BFF mantiene
/// después de que ese flujo terminó.
/// </summary>
public sealed class BffSessionOptions
{
    public const string SectionName = "Bff:Session";

    /// <summary>
    /// Nombre de la cookie de sesión que recibe el navegador. Su valor es siempre un identificador
    /// opaco (nunca un token ni datos serializados) -- ver <see cref="IBffSessionStore"/>.
    /// </summary>
    public string CookieName { get; set; } = "bc-bff-session";

    /// <summary>
    /// Vigencia por inactividad (expiración deslizante): cada request autenticado la renueva. Si el
    /// usuario no interactúa durante este período, la sesión expira aunque el access token del IdP
    /// siguiera siendo válido.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
