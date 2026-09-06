using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Mapea el proxy YARP del BFF exigiendo la sesión de cookie del BFF (F2-03,
/// <see cref="BffAuthenticationDefaults.Scheme"/>) para cualquier ruta configurada en
/// <c>"ReverseProxy"</c> (ver <see cref="BffProxyServiceCollectionExtensions.AddSharedBffProxy"/>): un
/// request sin sesión válida nunca llega a reenviarse a la API downstream, recibe <c>401</c> antes.
/// </summary>
public static class BffProxyEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSharedBffProxy(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapReverseProxy().RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(BffAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser());

        return endpoints;
    }
}
