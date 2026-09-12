using MyApp.Modules;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MyApp.Modules.HealthChecks;

/// <summary>Health check de readiness (mismo patrón que
/// <c>src/Platform/BitCode.Platform.Dashboard/HealthChecks/DashboardDbContextHealthCheck.cs</c>) para
/// <see cref="ModuleNameDbContext"/>.</summary>
internal sealed class ModuleNameDbContextHealthCheck(ModuleNameDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server disponible (ModuleName).")
                : HealthCheckResult.Unhealthy("SQL Server no responde (CanConnectAsync devolvió false).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.", ex);
        }
    }
}
