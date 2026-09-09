using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.Identity.HealthChecks;

/// <summary>
/// Health check de READINESS (mismo patrón que <c>DbContextHealthCheck</c> de
/// <c>Shared.Infrastructure.Persistence</c>, F1-25) para <see cref="IdentityAdministrationDbContext"/> --
/// esa clase es <c>internal</c> a su propio proyecto, así que este módulo declara su propia copia en vez
/// de referenciarla. Se registra siempre con el tag "ready" -- nunca en <c>/health/live</c>.
/// </summary>
internal sealed class IdentityAdministrationDbContextHealthCheck(IdentityAdministrationDbContext dbContext)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (Identity Administration).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
