using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.ImportExport.HealthChecks;

/// <summary>Health check de readiness (mismo patrón que <c>IntegrationHubDbContextHealthCheck</c>) para
/// <see cref="ImportExportDbContext"/>.</summary>
internal sealed class ImportExportDbContextHealthCheck(ImportExportDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (ImportExport).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
