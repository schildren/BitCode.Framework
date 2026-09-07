using Yarp.ReverseProxy.Transforms;

namespace BitCode.Gateway.Security;

/// <summary>
/// Headers (F4-08): remueve del request proxyado hacia el backend cualquier header interno/sensible
/// que un cliente externo no debería poder inyectar u observar reenviado -- defensa en profundidad,
/// independiente de que el backend real hoy no los use. No toca <c>Authorization</c> (el Gateway ya
/// validó el JWT como auth boundary antes de llegar acá, pero el backend puede seguir necesitando el
/// token para su propia evaluación RBAC/ABAC -- F2-07/F2-08 -- no hay motivo para quitarlo) ni los
/// headers <c>X-Forwarded-*</c> estándar (los agrega YARP automáticamente por ruta, ver
/// <c>appsettings.json</c>, "ReverseProxy:Clusters:*:HttpRequest"/transforms por defecto).
/// </summary>
public sealed class SensitiveHeaderSanitizingTransform : RequestTransform
{
    private static readonly string[] HeadersToStrip =
    [
        // Nombres de ejemplo de headers internos que un cliente externo nunca debe poder setear (p.
        // ej. usados por herramientas de diagnóstico/debug o por un futuro mecanismo interno
        // service-to-service) -- lista abierta, cada proyecto consumidor la extiende según su propia
        // superficie de headers internos.
        "X-Internal-Api-Key",
        "X-Debug-Token",
    ];

    public override ValueTask ApplyAsync(RequestTransformContext context)
    {
        foreach (var header in HeadersToStrip)
        {
            context.ProxyRequest.Headers.Remove(header);

            if (context.ProxyRequest.Content is not null)
            {
                context.ProxyRequest.Content.Headers.Remove(header);
            }
        }

        return ValueTask.CompletedTask;
    }
}
