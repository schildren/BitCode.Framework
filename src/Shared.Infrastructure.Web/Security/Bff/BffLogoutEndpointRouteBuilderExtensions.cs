using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Mapea el endpoint de logout del BFF (F2-03): cierra siempre la sesión server-side propia (revoca el
/// identificador de sesión en <c>IBffSessionStore</c> y expira la cookie), y opcionalmente devuelve la
/// URL de RP-Initiated Logout del IdP (<c>end_session_endpoint</c>, si el proveedor lo publica) para que
/// la SPA decida navegar allí y cerrar también la sesión SSO -- una respuesta JSON, no un <c>302</c>
/// server-side, porque este endpoint está pensado para invocarse con <c>fetch()</c> desde la SPA.
/// </summary>
public static class BffLogoutEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSharedBffLogout(this IEndpointRouteBuilder endpoints, string logoutPath = "/auth/logout")
    {
        endpoints.MapPost(logoutPath, async (
            HttpContext httpContext,
            IOidcDiscoveryDocumentProvider discoveryDocumentProvider,
            IOptions<OidcOptions> oidcOptions,
            IOptions<OidcAuthorizationCodeFlowOptions> flowOptions,
            CancellationToken cancellationToken) =>
        {
            // Se lee ANTES de cerrar sesión: una vez hecho SignOutAsync, la sesión (y el id_token que
            // guardaba) deja de existir en IBffSessionStore.
            var idToken = await httpContext.GetTokenAsync(BffAuthenticationDefaults.Scheme, "id_token");

            // Revoca la sesión server-side (IBffSessionStore.RemoveAsync vía BffTicketStore) y expira la
            // cookie -- a partir de este punto el navegador ya no tiene forma de volver a usarla, con o
            // sin logout del lado del IdP.
            await httpContext.SignOutAsync(BffAuthenticationDefaults.Scheme);

            var endSessionUri = await TryBuildEndSessionUriAsync(
                discoveryDocumentProvider, oidcOptions.Value, flowOptions.Value, idToken, cancellationToken);

            return endSessionUri is null
                ? Microsoft.AspNetCore.Http.Results.NoContent()
                : Microsoft.AspNetCore.Http.Results.Ok(new { idpEndSessionUri = endSessionUri });
        }).RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(BffAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser());

        return endpoints;
    }

    /// <summary>
    /// Mapea el endpoint de autoservicio "cerrar sesión en todos los dispositivos" (F2-06): revoca TODAS
    /// las sesiones BFF activas del sujeto autenticado que hace la llamada (<c>IBffSessionStore.
    /// RevokeAllForSubjectAsync</c>), no solo la sesión actual -- útil, por ejemplo, tras detectar que el
    /// usuario dejó una sesión abierta en un dispositivo compartido. Distinto de
    /// <see cref="MapSharedBffLogout"/>: ese cierra únicamente la sesión que hizo la llamada; este cierra
    /// todas las del mismo sujeto, incluida la que hizo la llamada. Un llamador solo puede revocar sus
    /// propias sesiones a través de este endpoint (el sujeto se toma del <c>ClaimsPrincipal</c>
    /// autenticado, nunca de un parámetro de la petición) -- una revocación administrativa de las
    /// sesiones de OTRO sujeto es un caso de uso distinto que debe exponerse detrás de una autorización
    /// explícita (RBAC/ABAC, Épica F2-B) invocando directamente <c>IBffSessionStore.
    /// RevokeAllForSubjectAsync</c>, no este endpoint de autoservicio.
    /// </summary>
    public static IEndpointRouteBuilder MapSharedBffLogoutAllDevices(this IEndpointRouteBuilder endpoints, string logoutAllDevicesPath = "/auth/logout-all")
    {
        endpoints.MapPost(logoutAllDevicesPath, async (
            HttpContext httpContext,
            IBffSessionStore sessionStore,
            CancellationToken cancellationToken) =>
        {
            var subject = httpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                // No debería ocurrir bajo RequireAuthenticatedUser() con una sesión creada por este BFF
                // (BffTicketStore siempre guarda "sub" si el id_token lo trajo) -- pero si faltara, cerrar
                // solo la sesión actual (comportamiento de MapSharedBffLogout) es más seguro que fallar
                // silenciosamente sin revocar nada.
                await httpContext.SignOutAsync(BffAuthenticationDefaults.Scheme);
                return Microsoft.AspNetCore.Http.Results.NoContent();
            }

            await sessionStore.RevokeAllForSubjectAsync(subject, cancellationToken);

            // La cookie del navegador que hizo esta llamada ya no resuelve a ninguna sesión (se revocó
            // como parte de RevokeAllForSubjectAsync) -- expirarla explícitamente evita depender de que
            // el próximo request falle su autenticación para "darse cuenta".
            await httpContext.SignOutAsync(BffAuthenticationDefaults.Scheme);

            return Microsoft.AspNetCore.Http.Results.NoContent();
        }).RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(BffAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser());

        return endpoints;
    }

    private static async Task<string?> TryBuildEndSessionUriAsync(
        IOidcDiscoveryDocumentProvider discoveryDocumentProvider,
        OidcOptions oidcOptions,
        OidcAuthorizationCodeFlowOptions flowOptions,
        string? idToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        OidcDiscoveryDocument discoveryDocument;
        try
        {
            discoveryDocument = await discoveryDocumentProvider.GetAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort: si la metadata del IdP no se puede resolver justo en este momento, el logout
            // local (ya hecho arriba) sigue siendo válido -- no bloquear el cierre de sesión del usuario
            // por un problema de red hacia el IdP.
            return null;
        }

        if (string.IsNullOrWhiteSpace(discoveryDocument.EndSessionEndpoint))
        {
            return null;
        }

        var queryParameters = new Dictionary<string, string?> { ["id_token_hint"] = idToken };
        if (!string.IsNullOrWhiteSpace(oidcOptions.ClientId))
        {
            queryParameters["client_id"] = oidcOptions.ClientId;
        }
        if (!string.IsNullOrWhiteSpace(flowOptions.PostLogoutRedirectUri))
        {
            queryParameters["post_logout_redirect_uri"] = flowOptions.PostLogoutRedirectUri;
        }

        return QueryHelpers.AddQueryString(discoveryDocument.EndSessionEndpoint!, queryParameters);
    }
}
