using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Transforms;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Transform de YARP (F2-03) que adjunta, como header <c>Authorization: Bearer</c>, el access token
/// guardado en la sesión server-side del usuario autenticado -- nunca uno que haya llegado en la
/// request original del navegador (que no debería traer ninguno: la SPA no tiene el token para
/// reenviarlo). Es la pieza concreta que conecta la sesión de cookie (F2-03) con las APIs protegidas
/// por el adapter OIDC (F2-01, <c>AddSharedOidcAuthentication</c>) sin que el navegador intervenga en
/// el transporte del token en ningún momento.
/// </summary>
/// <remarks>
/// Clase separada (en vez de un lambda inline en <c>AddReverseProxy().AddTransforms(...)</c>)
/// deliberadamente, para poder probar unitariamente "el proxy adjunta correctamente el token guardado"
/// sin necesitar un pipeline de YARP completo ni un servidor downstream real.
/// </remarks>
public sealed class BffAccessTokenRequestTransform : RequestTransform
{
    public override async ValueTask ApplyAsync(RequestTransformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var accessToken = await context.HttpContext.GetTokenAsync(BffAuthenticationDefaults.Scheme, "access_token");
        if (!string.IsNullOrEmpty(accessToken))
        {
            context.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        // Defensa en profundidad: si por algún motivo el navegador mandó su propio header Authorization
        // hacia el BFF (no debería, la SPA no tiene ningún token), YA fue sobrescrito arriba con el de
        // la sesión server-side -- nunca se reenvía tal cual algo que vino directo del cliente.
    }
}
