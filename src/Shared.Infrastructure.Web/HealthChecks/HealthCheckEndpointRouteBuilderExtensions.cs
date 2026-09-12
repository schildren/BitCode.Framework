using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;

namespace BitCode.Framework.Shared.Infrastructure.Web.HealthChecks;

/// <summary>
/// Mapea los dos endpoints estándar de health checks (F1-25) con semántica deliberadamente
/// distinta -- ver `docs/guia-health-checks.md` para el detalle de la decisión.
/// </summary>
public static class HealthCheckEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registra `/health/live` y `/health/ready`.
    /// <para>
    /// `/health/live` (LIVENESS) usa un <see cref="HealthCheckOptions.Predicate"/> que descarta
    /// TODOS los checks registrados (<c>_ => false</c>): `HealthCheckService` nunca invoca ningún
    /// delegado de chequeo para este endpoint, así que responde 200 mientras el proceso .NET esté
    /// vivo y pueda atender requests, sin tocar SQL Server, Redis ni ninguna otra dependencia
    /// externa. Un orquestador (Kubernetes) usa este endpoint para decidir si reinicia el proceso
    /// -- nunca debe hacerlo solo porque una dependencia externa esté caída, porque un restart no
    /// arregla que SQL Server no responda.
    /// </para>
    /// <para>
    /// `/health/ready` (READINESS) solo ejecuta los checks etiquetados <c>"ready"</c> -- el check
    /// de SQL Server que registra <c>AddSharedPersistence</c> (Shared.Infrastructure.Persistence,
    /// F1-25) y, si el proyecto configuró Redis, el que registra <c>AddSharedCaching</c>
    /// (Shared.Infrastructure.Caching). Responde 503 (default de `HealthCheckOptions` sin
    /// `ResponseWriter` propio) si alguna dependencia crítica falla -- un balanceador/orquestador
    /// debe sacar la instancia del pool de tráfico hasta que se recupere.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapSharedHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        });

        return endpoints;
    }
}
