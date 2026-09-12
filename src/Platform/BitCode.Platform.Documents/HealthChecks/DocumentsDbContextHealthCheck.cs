using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BitCode.Framework.Platform.Documents.HealthChecks;

/// <summary>
/// Health check de READINESS (mismo patrón que <c>FeatureManagementDbContextHealthCheck</c>/
/// <c>CatalogsDbContextHealthCheck</c>, F1-25) para <see cref="DocumentsDbContext"/>. Se registra siempre
/// con el tag "ready" -- nunca en <c>/health/live</c>.
/// </summary>
internal sealed class DocumentsDbContextHealthCheck(DocumentsDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (Documents).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
