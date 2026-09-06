using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.HealthChecks;

/// <summary>
/// Health check de READINESS (F1-25) para el `DbContext` registrado por `AddSharedPersistence`:
/// confirma que SQL Server acepta conexión (`Database.CanConnectAsync`) sin ejecutar ninguna
/// consulta de negocio. Se registra siempre con el tag "ready" -- nunca debe incluirse en el
/// endpoint de LIVENESS (`/health/live`), que por diseño no depende de ninguna infraestructura
/// externa (ver `docs/guia-health-checks.md`).
/// </summary>
internal sealed class DbContextHealthCheck(DbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible.")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
