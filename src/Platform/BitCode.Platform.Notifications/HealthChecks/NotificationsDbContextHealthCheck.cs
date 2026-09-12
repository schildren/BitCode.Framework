using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.Notifications.HealthChecks;

/// <summary>Health check de readiness (mismo patrón que <c>WorkflowDbContextHealthCheck</c>/
/// <c>TaskInboxDbContextHealthCheck</c>) para <see cref="NotificationsDbContext"/>.</summary>
internal sealed class NotificationsDbContextHealthCheck(NotificationsDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (Notifications).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
