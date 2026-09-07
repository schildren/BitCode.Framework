using Microsoft.AspNetCore.Builder;

namespace BitCode.Gateway.RequestLimits;

public static class MaxRequestBodySizeMiddlewareExtensions
{
    /// <summary>
    /// Aplica <see cref="MaxRequestBodySizeMiddleware"/> a todo el pipeline del Gateway -- debe ir antes
    /// de <c>UseAuthentication</c>/<c>UseRateLimiter</c>/<c>MapReverseProxy</c> (Program.cs) para
    /// rechazar un payload sobredimensionado sin gastar trabajo de autenticación/proxy en él.
    /// </summary>
    public static IApplicationBuilder UseGatewayMaxRequestBodySize(this IApplicationBuilder app) =>
        app.UseMiddleware<MaxRequestBodySizeMiddleware>();
}
