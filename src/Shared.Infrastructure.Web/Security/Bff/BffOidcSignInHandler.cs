using System.Security.Claims;
using System.Text.Json;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Implementación de referencia del delegado <c>onSignedIn</c> de
/// <c>MapSharedOidcAuthorizationCodeLogin</c> (F2-02): convierte el resultado del intercambio de code
/// en la sesión de cookie del BFF (F2-03) en vez de devolver los tokens al navegador. Es exactamente el
/// punto de apoyo que F2-02 dejó preparado a propósito -- ver el comentario de
/// <c>OidcAuthorizationCodeEndpointRouteBuilderExtensions</c>.
/// </summary>
/// <remarks>
/// Registro típico:
/// <code>
/// app.MapSharedOidcAuthorizationCodeLogin(onSignedIn: BffOidcSignInHandler.HandleAsync);
/// </code>
/// </remarks>
public static class BffOidcSignInHandler
{
    /// <summary>
    /// Claims técnicos del id_token que no se copian a la sesión del BFF -- ya cumplieron su función al
    /// validar el propio flujo (o son metadata de protocolo sin valor para el resto de la aplicación).
    /// </summary>
    private static readonly HashSet<string> ExcludedClaimTypes = new(StringComparer.Ordinal)
    {
        "iss", "aud", "exp", "iat", "auth_time", "nonce", "azp", "at_hash", "c_hash", "sid", "jti", "nbf",
    };

    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        OidcTokenResponse tokens,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(tokens);

        if (string.IsNullOrWhiteSpace(tokens.IdToken))
        {
            // El scope "openid" (obligatorio en OidcAuthorizationCodeFlowOptions) garantiza un id_token
            // en un intercambio exitoso conforme al estándar -- llegar sin uno es una configuración de
            // scope incorrecta, no un caso de negocio a tolerar silenciosamente.
            return Result.Failure(Error.Failure(
                "Bff.MissingIdToken",
                "El IdP no devolvió id_token: revisar que 'Oidc:AuthorizationCode:Scope' incluya 'openid'.")).ToProblemDetails();
        }

        var claims = ExtractIdTokenClaims(tokens.IdToken);
        var identity = new ClaimsIdentity(claims, BffAuthenticationDefaults.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties { IsPersistent = true };
        var authTokens = new List<AuthenticationToken>
        {
            new() { Name = BffTokenNames.AccessToken, Value = tokens.AccessToken },
            new() { Name = BffTokenNames.IdToken, Value = tokens.IdToken },
        };
        if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            authTokens.Add(new AuthenticationToken { Name = BffTokenNames.RefreshToken, Value = tokens.RefreshToken });
        }
        if (tokens.ExpiresInSeconds is { } expiresInSeconds)
        {
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
            authTokens.Add(new AuthenticationToken { Name = BffTokenNames.ExpiresAt, Value = expiresAt.ToString("o") });
        }
        properties.StoreTokens(authTokens);

        // SignInAsync delega en CookieAuthenticationOptions.SessionStore (BffTicketStore) -- el
        // navegador recibe únicamente la cookie de sesión con el identificador opaco que devuelve
        // IBffSessionStore.CreateAsync, nunca este AuthenticationTicket ni los tokens que contiene.
        await httpContext.SignInAsync(BffAuthenticationDefaults.Scheme, principal, properties);

        return Microsoft.AspNetCore.Http.Results.Redirect(SafeLocalRedirectTarget(returnUrl));
    }

    /// <summary>
    /// Decodifica (sin validar firma) el payload del id_token para extraer sus claims. No es una
    /// omisión: el id_token llegó server-side, directo del <c>token_endpoint</c> del IdP por una
    /// conexión TLS saliente autenticada con PKCE (F2-02) -- nunca a través del navegador -- por lo que
    /// no hace falta revalidar su firma para confiar en su contenido en este punto exacto del flujo
    /// (la validación completa de firma/issuer/audiencia/vigencia para tokens que SÍ llegan por una ruta
    /// no confiable, p. ej. un Bearer recibido en un endpoint de API, es exactamente el alcance de F2-05).
    /// </summary>
    private static IEnumerable<Claim> ExtractIdTokenClaims(string idToken)
    {
        var segments = idToken.Split('.');
        if (segments.Length < 2)
        {
            yield break;
        }

        using var payload = JsonDocument.Parse(Base64UrlDecode(segments[1]));
        foreach (var property in payload.RootElement.EnumerateObject())
        {
            if (ExcludedClaimTypes.Contains(property.Name))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    yield return new Claim(property.Name, item.ToString());
                }
            }
            else if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                yield return new Claim(property.Name, property.Value.ToString());
            }
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    /// <summary>
    /// Igual criterio que <c>Microsoft.AspNetCore.Mvc.LocalRedirectResult</c>/<c>IsLocalUrl</c>: solo
    /// permite redirigir a una ruta relativa propia del mismo origen -- nunca a una URL absoluta ni a
    /// una ruta "protocol-relative" (<c>//evil.com</c>), que es el vector clásico de open redirect si
    /// <c>returnUrl</c> (controlado por el usuario en <c>/auth/login?returnUrl=...</c>) se reflejara sin
    /// validar.
    /// </summary>
    private static string SafeLocalRedirectTarget(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var isLocal = returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal);

        return isLocal ? returnUrl : "/";
    }
}
