using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.Reporting.HealthChecks;

/// <summary>Health check de readiness (mismo patrón que <c>TaskInboxDbContextHealthCheck</c>) para
/// <see cref="ReportingDbContext"/>.</summary>
internal sealed class ReportingDbContextHealthCheck(ReportingDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (Reporting).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
