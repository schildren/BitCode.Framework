using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// Criterio de aceptación de F1-25 ("dependencias críticas evaluadas correctamente") verificado
/// contra un SQL Server real (Testcontainers): el check "sql-server" que registra
/// <c>AddSharedPersistence</c> reporta <see cref="HealthStatus.Healthy"/> cuando la base es
/// alcanzable y <see cref="HealthStatus.Unhealthy"/> cuando no lo es -- sin lanzar una excepción sin
/// controlar hacia el llamador (`HealthCheckService`/el endpoint HTTP siempre reciben un
/// <see cref="HealthReport"/>, nunca una excepción). El endpoint HTTP (`/health/ready` vs
/// `/health/live`, Shared.Infrastructure.Web) se verifica por separado, con checks falsos, en
/// <c>HealthCheckEndpointRouteBuilderExtensionsTests</c> -- esta prueba se enfoca exclusivamente en
/// que el check real de SQL Server refleje correctamente la disponibilidad de la dependencia.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DbContextHealthCheckIntegrationTests(SqlServerContainerFixture fixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        fixture.BuildIsolatedConnectionString("HealthChecks", testName);

    /// <summary>
    /// Connection string que apunta a un puerto local sin nada escuchando -- falla rápido
    /// ("conexión rechazada") en vez de esperar el ConnectTimeout completo, para que el test no sea
    /// lento ni flaky.
    /// </summary>
    private static string BuildUnreachableConnectionString(string baseConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            DataSource = "127.0.0.1,59999",
            ConnectTimeout = 2,
        };

        return builder.ConnectionString;
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SqlServerCheck_ConSqlServerDisponible_ReportaHealthy()
    {
        var connectionString = BuildIsolatedConnectionString();
        await using var provider = await BuildProviderAsync(connectionString);

        await using (var setupScope = provider.CreateAsyncScope())
        {
            await setupScope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>().Database.EnsureCreatedAsync();
        }

        var healthCheckService = provider.GetRequiredService<HealthCheckService>();
        var report = await healthCheckService.CheckHealthAsync(registration => registration.Tags.Contains("ready"));

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().ContainKey("sql-server");
        report.Entries["sql-server"].Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task SqlServerCheck_ConSqlServerInalcanzable_ReportaUnhealthy_SinLanzarExcepcion()
    {
        var connectionString = BuildUnreachableConnectionString(fixture.ConnectionString);
        await using var provider = await BuildProviderAsync(connectionString);

        var healthCheckService = provider.GetRequiredService<HealthCheckService>();
        var report = await healthCheckService.CheckHealthAsync(registration => registration.Tags.Contains("ready"));

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["sql-server"].Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["sql-server"].Description.Should().NotBeNullOrWhiteSpace(
            "el check debe describir por qué SQL Server no está disponible, tanto si " +
            "CanConnectAsync devuelve false como si lanza una excepción de conexión -- en ningún " +
            "caso debe dejar la excepción propagar sin controlar hacia HealthCheckService");
    }
}
