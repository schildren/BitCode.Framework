using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace BitCode.Gateway.RequestLimits;

/// <summary>
/// Rechaza con <c>413 Payload Too Large</c> cualquier request cuyo <c>Content-Length</c> declarado
/// supere <see cref="GatewayRequestLimitsOptions.MaxRequestBodySizeBytes"/> -- ANTES de autenticar o de
/// proxyar (mismo criterio de "rechazar barato antes de trabajo caro" que ya aplica el rate limiting de
/// F4-08). Cubre el caso común (un cliente abusivo declara el tamaño real del payload en el header).
/// Para el caso sin <c>Content-Length</c> (chunked transfer encoding), ajusta
/// <see cref="IHttpMaxRequestBodySizeFeature"/> para que Kestrel corte la conexión si el cuerpo real
/// termina superando el límite durante la lectura -- ese caso no se puede rechazar de forma anticipada
/// porque el tamaño real no se conoce hasta leer el stream.
/// </summary>
public sealed class MaxRequestBodySizeMiddleware(
    RequestDelegate next,
    IOptionsMonitor<GatewayRequestLimitsOptions> optionsMonitor)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var maxRequestBodySizeBytes = optionsMonitor.CurrentValue.MaxRequestBodySizeBytes;

        if (context.Request.ContentLength is { } contentLength && contentLength > maxRequestBodySizeBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySizeFeature is { IsReadOnly: false })
        {
            maxRequestBodySizeFeature.MaxRequestBodySize = maxRequestBodySizeBytes;
        }

        await next(context);
    }
}
