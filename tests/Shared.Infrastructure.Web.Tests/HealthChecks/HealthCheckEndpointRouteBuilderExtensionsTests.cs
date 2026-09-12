using System.Net;
using BitCode.Framework.Shared.Infrastructure.Web.HealthChecks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.HealthChecks;

/// <summary>
/// Verifica el criterio de aceptación de F1-25 ("dependencias críticas evaluadas correctamente")
/// a nivel de endpoint HTTP, sin depender de Docker/SQL Server real: un
/// <see cref="FakeCriticalDependencyHealthCheck"/> etiquetado "ready" simula la dependencia crítica
/// (rol que en un proyecto real cumplen el check de SQL Server de
/// <c>AddSharedPersistence</c>/Shared.Infrastructure.Persistence y el de Redis de
/// <c>AddSharedCaching</c>/Shared.Infrastructure.Caching, verificados contra infraestructura real en
/// <c>DbContextHealthCheckIntegrationTests</c>).
/// </summary>
public class HealthCheckEndpointRouteBuilderExtensionsTests
{
    /// <summary>
    /// Health check de prueba con Status mutable y contador de invocaciones -- permite demostrar
    /// que <c>/health/live</c> jamás ejecuta el delegado de ningún check registrado (Predicate
    /// descarta todos), en vez de solo observar el código de estado HTTP resultante.
    /// </summary>
    private sealed class FakeCriticalDependencyHealthCheck : IHealthCheck
    {
        public HealthStatus Status { get; set; } = HealthStatus.Healthy;

        public int InvocationCount { get; private set; }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(new HealthCheckResult(Status));
        }
    }

    private static async Task<(TestServer Server, FakeCriticalDependencyHealthCheck Check)> CreateServerAsync()
    {
        var check = new FakeCriticalDependencyHealthCheck();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddHealthChecks()
            .AddCheck("fake-critical-dependency", check, tags: ["ready"]);

        var app = builder.Build();
        app.MapSharedHealthChecks();

        await app.StartAsync();
        var testServer = (TestServer)app.Services.GetRequiredService<IServer>();

        return (testServer, check);
    }

    [Fact]
    public async Task Live_SiempreRespondeOk_SinInvocarNingunHealthCheckRegistrado()
    {
        var (server, check) = await CreateServerAsync();
        check.Status = HealthStatus.Unhealthy; // simula SQL Server/Redis caídos
        using var client = server.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "liveness nunca debe depender de que una dependencia externa esté disponible");
        check.InvocationCount.Should().Be(
            0,
            "el Predicate de /health/live descarta todos los checks -- HealthCheckService no debe " +
            "ejecutar ningún delegado, ni siquiera para reportarlo como Unhealthy");
    }

    [Fact]
    public async Task Ready_ConDependenciaCriticaSaludable_RespondeOk()
    {
        var (server, check) = await CreateServerAsync();
        check.Status = HealthStatus.Healthy;
        using var client = server.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        check.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task Ready_ConDependenciaCriticaCaida_Retorna503()
    {
        var (server, check) = await CreateServerAsync();
        check.Status = HealthStatus.Unhealthy;
        using var client = server.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        check.InvocationCount.Should().Be(1);
    }
}
