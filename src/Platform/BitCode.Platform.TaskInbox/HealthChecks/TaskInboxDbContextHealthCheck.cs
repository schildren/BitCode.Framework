using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.TaskInbox.HealthChecks;

/// <summary>Health check de readiness (mismo patrón que <c>WorkflowDbContextHealthCheck</c>) para
/// <see cref="TaskInboxDbContext"/>.</summary>
internal sealed class TaskInboxDbContextHealthCheck(TaskInboxDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (Task Inbox).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
