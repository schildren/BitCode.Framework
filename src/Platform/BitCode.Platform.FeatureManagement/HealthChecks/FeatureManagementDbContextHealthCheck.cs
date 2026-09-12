using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.FeatureManagement.HealthChecks;

/// <summary>
/// Health check de READINESS (mismo patrón que <c>CatalogsDbContextHealthCheck</c>/
/// <c>OrganizationDbContextHealthCheck</c>, F1-25) para <see cref="FeatureManagementDbContext"/>. Se
/// registra siempre con el tag "ready" -- nunca en <c>/health/live</c>.
/// </summary>
internal sealed class FeatureManagementDbContextHealthCheck(FeatureManagementDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (Feature Management).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
