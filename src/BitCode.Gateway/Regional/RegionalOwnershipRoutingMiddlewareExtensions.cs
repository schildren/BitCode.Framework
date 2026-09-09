using Microsoft.AspNetCore.Builder;

namespace BitCode.Gateway.Regional;

public static class RegionalOwnershipRoutingMiddlewareExtensions
{
    /// <summary>
    /// Aplica <see cref="RegionalOwnershipRoutingMiddleware"/> (F5-03) -- debe ir después de
    /// <c>UseAuthentication</c>/<c>UseAuthorization</c> (necesita el claim <c>tenant_id</c> del usuario
    /// ya autenticado) y antes de <c>MapReverseProxy</c> (Program.cs).
    /// </summary>
    public static IApplicationBuilder UseGatewayRegionalOwnershipRouting(this IApplicationBuilder app) =>
        app.UseMiddleware<RegionalOwnershipRoutingMiddleware>();
}
