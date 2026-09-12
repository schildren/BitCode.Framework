using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.IntegrationHub.HealthChecks;

/// <summary>Health check de readiness (mismo patrón que <c>NotificationsDbContextHealthCheck</c>) para
/// <see cref="IntegrationHubDbContext"/>.</summary>
internal sealed class IntegrationHubDbContextHealthCheck(IntegrationHubDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (IntegrationHub).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
