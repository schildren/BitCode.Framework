using BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Registra la autenticación por cookie del BFF (F2-03): el mecanismo que hace que el navegador nunca
/// reciba un access/refresh/id token. Reutiliza el flujo Authorization Code + PKCE de F2-02
/// (<c>MapSharedOidcAuthorizationCodeLogin</c>) pasándole <see cref="BffOidcSignInHandler.HandleAsync"/>
/// como <c>onSignedIn</c> -- ese delegado es quien convierte los tokens del IdP en esta sesión de cookie.
/// </summary>
public static class BffAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Registra <c>IBffSessionStore</c> (Shared.Infrastructure.Security) y el esquema de autenticación
    /// por cookie <see cref="BffAuthenticationDefaults.Scheme"/> respaldado por <see cref="BffTicketStore"/>:
    /// la cookie que recibe el navegador (<c>Bff:Session:CookieName</c>, por defecto <c>"bc-bff-session"</c>)
    /// es HttpOnly + Secure + SameSite=Strict y contiene únicamente un identificador de sesión opaco
    /// -- nunca los tokens, que quedan exclusivamente en <c>IBffSessionStore</c> server-side.
    /// </summary>
    /// <remarks>
    /// Un endpoint AJAX del proxy del BFF (<c>MapSharedBffProxy</c>) sin sesión válida recibe <c>401</c>
    /// (no un redirect HTML a una página de login, que no tiene sentido para una llamada <c>fetch()</c>
    /// de la SPA) -- la SPA es quien decide navegar a <c>/auth/login</c> cuando corresponda.
    /// </remarks>
    public static IServiceCollection AddSharedBffCookieAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedBffSessionStore(configuration);

        services
            .AddAuthentication(BffAuthenticationDefaults.Scheme)
            .AddCookie(BffAuthenticationDefaults.Scheme, options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.SlidingExpiration = true;

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        // PostConfigure: el cookie name y la vigencia salen de BffSessionOptions (una sola fuente de
        // verdad, compartida con IBffSessionStore) y el SessionStore necesita IBffSessionStore
        // resuelto del contenedor -- ninguno de los dos está disponible dentro del delegate de AddCookie.
        services.AddOptions<CookieAuthenticationOptions>(BffAuthenticationDefaults.Scheme)
            .Configure<IBffSessionStore, IOptions<BffSessionOptions>>((cookieOptions, sessionStore, bffSessionOptions) =>
            {
                var sessionOptions = bffSessionOptions.Value;
                cookieOptions.Cookie.Name = sessionOptions.CookieName;
                cookieOptions.ExpireTimeSpan = sessionOptions.IdleTimeout;
                cookieOptions.SessionStore = new BffTicketStore(sessionStore, sessionOptions.IdleTimeout);
            });

        return services;
    }
}
