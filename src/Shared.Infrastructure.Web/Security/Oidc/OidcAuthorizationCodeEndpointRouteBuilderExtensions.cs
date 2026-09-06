using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Oidc;

/// <summary>
/// Mapea los dos endpoints del flujo Authorization Code + PKCE (F2-02) para usuarios web:
/// <c>/auth/login</c> (inicia el flujo, redirige al IdP) y <c>/auth/callback</c> (recibe el <c>code</c>,
/// lo intercambia por tokens). Cimiento reutilizable de F2-03 (BFF): esta clase nunca escribe una
/// sesión ni expone tokens directamente al navegador -- delega esa decisión al delegado
/// <c>onSignedIn</c> que el proyecto consumidor (o F2-03) provee explícitamente, precisamente para que
/// F2-03 pueda convertir el resultado del intercambio en una sesión server-side sin que este método
/// tenga que rediseñarse.
/// </summary>
/// <remarks>
/// Requiere que el proyecto consumidor haya llamado antes
/// <c>services.AddSharedOidcAuthorizationCodeFlow(configuration)</c> (Shared.Infrastructure.Security) --
/// resuelve <c>IOidcAuthorizationRequestFactory</c>/<c>IOidcAuthorizationCodeStateProtector</c>/
/// <c>IOidcAuthorizationCodeExchanger</c> desde el contenedor de DI; sin ese registro, resolver
/// cualquiera de esos servicios lanza al primer request a <c>/auth/login</c>.
/// </remarks>
public static class OidcAuthorizationCodeEndpointRouteBuilderExtensions
{
    /// <summary>Cookie HttpOnly de un solo uso -- nunca legible ni manipulable desde JavaScript de la SPA.</summary>
    internal const string CorrelationCookieName = "bc-oidc-cx";

    /// <param name="onSignedIn">
    /// Se invoca tras un intercambio de code exitoso, con los tokens obtenidos y el <c>returnUrl</c>
    /// original (si se pasó como query string a <c>/auth/login</c>). Decide la respuesta HTTP final --
    /// p. ej. F2-03 (BFF) la usará para emitir una cookie de sesión propia y redirigir a la SPA sin que
    /// el navegador vea nunca el access/id/refresh token.
    /// </param>
    public static IEndpointRouteBuilder MapSharedOidcAuthorizationCodeLogin(
        this IEndpointRouteBuilder endpoints,
        Func<HttpContext, OidcTokenResponse, string?, CancellationToken, Task<IResult>> onSignedIn,
        string loginPath = "/auth/login",
        string callbackPath = "/auth/callback")
    {
        ArgumentNullException.ThrowIfNull(onSignedIn);

        endpoints.MapGet(loginPath, async (
            HttpContext httpContext,
            IOidcAuthorizationRequestFactory authorizationRequestFactory,
            IOidcAuthorizationCodeStateProtector stateProtector,
            IOptions<OidcAuthorizationCodeFlowOptions> flowOptions,
            string? returnUrl,
            CancellationToken cancellationToken) =>
        {
            var authorizationRequest = await authorizationRequestFactory.CreateAsync(returnUrl, cancellationToken);
            var protectedState = stateProtector.Protect(authorizationRequest.State);

            httpContext.Response.Cookies.Append(CorrelationCookieName, protectedState, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = flowOptions.Value.CorrelationCookieLifetime,
                IsEssential = true,
            });

            // 302 hacia el authorization_endpoint del IdP -- response_type=code únicamente (lo construye
            // IOidcAuthorizationRequestFactory), jamás "token"/"id_token": sin flujo implícito.
            return Microsoft.AspNetCore.Http.Results.Redirect(authorizationRequest.AuthorizationUri.ToString());
        });

        endpoints.MapGet(callbackPath, async (
            HttpContext httpContext,
            IOidcAuthorizationCodeStateProtector stateProtector,
            IOidcAuthorizationCodeExchanger exchanger,
            string? code,
            string? state,
            string? error,
            string? error_description,
            CancellationToken cancellationToken) =>
        {
            var hasCookie = httpContext.Request.Cookies.TryGetValue(CorrelationCookieName, out var protectedState);
            // Un solo uso: se borra apenas se lee, exitoso o no el resto del procesamiento.
            httpContext.Response.Cookies.Delete(CorrelationCookieName);

            if (!string.IsNullOrEmpty(error))
            {
                return Result.Failure(Error.Unauthorized(
                    "Oidc.AuthorizationError",
                    error_description ?? $"El IdP rechazó la autorización: {error}.")).ToProblemDetails();
            }

            if (!hasCookie || string.IsNullOrEmpty(protectedState))
            {
                return Result.Failure(Error.Unauthorized(
                    "Oidc.MissingCorrelation",
                    "Falta la cookie de correlación del flujo de login (expiró, fue borrada o el callback llegó sin pasar por /auth/login).")).ToProblemDetails();
            }

            var correlationState = stateProtector.Unprotect(protectedState);
            if (correlationState is null)
            {
                return Result.Failure(Error.Unauthorized(
                    "Oidc.InvalidCorrelation",
                    "La cookie de correlación no se pudo validar (corrupta, alterada o de otra clave de Data Protection).")).ToProblemDetails();
            }

            // Comparación del "state" (RFC 6749 sección 10.12): un token de correlación de alta entropía
            // y de un solo uso -- no es una comparación de secreto tipo password, por lo que la igualdad
            // ordinal estándar es suficiente (no hace falta comparación en tiempo constante).
            if (string.IsNullOrEmpty(state) || !string.Equals(state, correlationState.State, StringComparison.Ordinal))
            {
                return Result.Failure(Error.Unauthorized(
                    "Oidc.InvalidState",
                    "El parámetro 'state' del callback no coincide con el emitido en /auth/login (posible CSRF).")).ToProblemDetails();
            }

            if (string.IsNullOrEmpty(code))
            {
                return Result.Failure(Error.Validation(
                    "Oidc.MissingCode",
                    "El callback no incluyó el parámetro 'code'.")).ToProblemDetails();
            }

            var exchangeResult = await exchanger.ExchangeAsync(
                code,
                correlationState.CodeVerifier,
                correlationState.RedirectUri,
                cancellationToken);

            if (exchangeResult.IsFailure)
            {
                return exchangeResult.ToProblemDetails();
            }

            return await onSignedIn(httpContext, exchangeResult.Value, correlationState.ReturnUrl, cancellationToken);
        });

        return endpoints;
    }
}
