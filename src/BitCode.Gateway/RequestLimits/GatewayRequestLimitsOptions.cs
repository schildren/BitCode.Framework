namespace BitCode.Gateway.RequestLimits;

/// <summary>
/// Límite máximo de tamaño de request body aplicado por el propio Gateway (F4-09) -- defensa en
/// profundidad de APLICACIÓN, complementaria (no sustituta) del límite equivalente que se declara a
/// nivel de Ingress/WAF perimetral (<c>nginx.ingress.kubernetes.io/proxy-body-size</c>, ver
/// <c>k8s/gateway/ingress.yaml</c> y <c>docs/politica-perimetral-waf.md</c>). Un consumidor que exponga
/// el Gateway sin ese Ingress delante (p. ej. detrás de un LoadBalancer directo, en un entorno de
/// pruebas, o mientras el WAF perimetral real todavía no está desplegado) sigue protegido por este
/// límite -- mismo criterio que <c>GatewayRateLimitingOptions</c> (F4-08) documenta para el rate
/// limiting de aplicación.
/// </summary>
public sealed class GatewayRequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>
    /// Tamaño máximo permitido del cuerpo de un request, en bytes. Default 10 MiB: generoso para
    /// payloads JSON/form típicos de una API, sin habilitar subidas de archivos grandes sin una
    /// decisión explícita del proyecto consumidor (que puede subir este valor por configuración si
    /// su caso de uso real lo requiere).
    /// </summary>
    public long MaxRequestBodySizeBytes { get; set; } = 10 * 1024 * 1024;
}
