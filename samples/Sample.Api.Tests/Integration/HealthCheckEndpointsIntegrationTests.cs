using System.Net;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sample.Api.Tests.Integration;

/// <summary>
/// Verifica F1-25 de punta a punta contra el proyecto piloto real (SQL Server vía Testcontainers):
/// `InfrastructureModule` cablea `AddSharedPersistence` (registra el check "sql-server") y
/// `app.MapSharedHealthChecks()` (Shared.Infrastructure.Web) tal como lo haría un consumidor real.
/// El caso "SQL Server caído" con el efecto correspondiente en `/health/ready` se verifica de forma
/// aislada y determinística (sin depender de parar/arrancar un contenedor a mitad de un test) en
/// `DbContextHealthCheckIntegrationTests` (Shared.Infrastructure.Persistence.Tests) -- acá solo se
/// confirma el camino feliz end-to-end: con SQL Server real disponible, ambos endpoints responden
/// 200, y `/health/live` no requiere que la base de datos exista siquiera (se llama ANTES del
/// `EnsureCreatedAsync` de <c>Program.cs</c>).
/// </summary>
[Collection(SampleApiSequentialCollection.Name)]
public class HealthCheckEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleApiHealthChecks", nameof(HealthCheckEndpointsIntegrationTests)));

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task HealthLive_ConSqlServerDisponible_RespondeOk()
    {
        var response = await _client!.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_ConSqlServerDisponible_RespondeOk()
    {
        var response = await _client!.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sqlServerFixture.DisposeAsync();
    }
}
