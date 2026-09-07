namespace BitCode.Gateway.RateLimiting;

/// <summary>
/// Política de rate limiting nativa de ASP.NET Core (<c>Microsoft.AspNetCore.RateLimiting</c>, F4-08)
/// aplicada al proxy YARP -- fixed window global, configurable por sección "RateLimiting" en vez de
/// hardcodeada. No reemplaza ningún límite/WAF externo (fuera de alcance, F4-09): esto es la
/// protección de aplicación del propio Gateway.
/// </summary>
public sealed class GatewayRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Cantidad máxima de requests permitidos por ventana.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Duración de la ventana fija, en segundos.</summary>
    public int WindowSeconds { get; set; } = 10;

    /// <summary>
    /// Cantidad de requests que se encolan (en vez de rechazarse inmediatamente) al superar
    /// <see cref="PermitLimit"/> dentro de la ventana vigente.
    /// </summary>
    public int QueueLimit { get; set; }
}
