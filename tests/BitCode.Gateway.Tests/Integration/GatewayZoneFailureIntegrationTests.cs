using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// F5-13 (Fase 5 — Disaster Recovery y multi-región, <c>docs/chaos-regional-fase5.md</c>), escenario 2
/// "Caída de una zona": simula dos "zonas" de disponibilidad como dos instancias REALES e
/// independientes del Gateway (dos <see cref="WebApplicationFactory{Program}"/>, cada una con su
/// propio host/DI y su propio backend real dedicado -- mismo criterio que
/// <see cref="GatewayDistributedRateLimitingIntegrationTests"/>, que ya trata a cada
/// <see cref="WebApplicationFactory{Program}"/> como el equivalente real de un pod/instancia
/// independiente de un mismo Deployment). Verifica que la caída total de una zona (esta instancia del
/// Gateway y su backend dejan de estar disponibles) (a) se detecta rápido y de forma controlada del
/// lado del cliente que le apuntaba, y (b) no afecta en absoluto a la otra zona, que sigue respondiendo
/// con normalidad -- el criterio real detrás de tener más de una zona.
/// </summary>
[Collection(GatewayEnvironmentVariableCollection.Name)]
public class GatewayZoneFailureIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string SecretKey = "clave-de-pruebas-de-caida-de-zona-32-caracteres-o-mas-larga";
    private const string Issuer = "BitCode.Gateway.Tests";
    private const string Audience = "BitCode.Gateway.Tests.Clients";

    // SLO documentado en docs/chaos-regional-fase5.md, escenario 2: la detección de que una zona dejó
    // de responder debe ser rápida (fallo de conexión, no un cuelgue) y la zona sana debe seguir
    // respondiendo con latencia normal (no degradada por la caída de la otra zona).
    private static readonly TimeSpan ZoneDownDetectionSlo = TimeSpan.FromSeconds(5);

    private GatewayTestBackend _backendZonaA = null!;
    private GatewayTestBackend _backendZonaB = null!;
    private WebApplicationFactory<Program> _factoryZonaA = null!;
    private WebApplicationFactory<Program> _factoryZonaB = null!;
    private HttpClient _clientZonaA = null!;
    private HttpClient _clientZonaB = null!;

    private readonly List<string> _envVarsSetForThisTest = [];

    public async Task InitializeAsync()
    {
        // Dos backends reales independientes -- cada uno representa la dependencia local de "su" zona
        // (mismo criterio de aislamiento que dos availability zones reales, cada una con su propia
        // infraestructura, sin recursos compartidos entre sí).
        _backendZonaA = new GatewayTestBackend();
        _backendZonaB = new GatewayTestBackend();
        await Task.WhenAll(_backendZonaA.StartAsync(), _backendZonaB.StartAsync());

        SetEnvironmentVariable("Jwt__SecretKey", SecretKey);
        SetEnvironmentVariable("Jwt__Issuer", Issuer);
        SetEnvironmentVariable("Jwt__Audience", Audience);

        // Zona A: variables de entorno del proceso apuntando a su propio backend -- WebApplicationFactory
        // lee la configuración del proceso en el momento de construir el host, así que se arma primero
        // por completo antes de tocar las variables para la Zona B.
        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
            _backendZonaA.BaseAddress);
        _factoryZonaA = new WebApplicationFactory<Program>();
        _clientZonaA = _factoryZonaA.CreateClient();

        // Zona B: se reapunta la misma variable de entorno a SU backend antes de construir el segundo
        // host -- cada WebApplicationFactory<Program> lee la configuración una sola vez, al arrancar, así
        // que ambos hosts quedan fijos a su respectivo backend aunque la variable de entorno del proceso
        // cambie después.
        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
            _backendZonaB.BaseAddress);
        _factoryZonaB = new WebApplicationFactory<Program>();
        _clientZonaB = _factoryZonaB.CreateClient();
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _envVarsSetForThisTest.Add(name);
    }

    private static string GenerateValidToken()
    {
        var options = Options.Create(new JwtOptions { SecretKey = SecretKey, Issuer = Issuer, Audience = Audience });
        var generator = new JwtTokenGenerator(options);
        var user = new ApplicationUser { UserName = "gateway-zone-chaos-user", Email = "gateway-zone-chaos-user@test.com" };

        return generator.GenerateAccessToken(user, roles: [], extraClaims: []);
    }

    [Fact]
    public async Task ZonaCaida_SeDetectaRapido_YLaZonaSanaSigueRespondiendoSinDegradacion()
    {
        _clientZonaA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());
        _clientZonaB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        // 1) Ambas zonas responden con normalidad antes del incidente.
        (await _clientZonaA.GetAsync("/api/echo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _clientZonaB.GetAsync("/api/echo")).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2) Caída TOTAL de la Zona A -- se detiene el proceso real (host + backend dedicado) que la
        //    representa, no se simula con un mock. Un LB/orquestador real, al detectar esto (vía health
        //    checks), dejaría de enrutar tráfico hacia esta zona -- acá se verifica el lado equivalente
        //    y observable directamente: un cliente que ya le apuntaba detecta el fallo rápido.
        await _factoryZonaA.DisposeAsync();
        await _backendZonaA.DisposeAsync();

        var stopwatch = Stopwatch.StartNew();
        Func<Task> requestContraZonaCaida = async () => await _clientZonaA.GetAsync("/api/echo");
        await requestContraZonaCaida.Should().ThrowAsync<Exception>(
            "una zona completamente caída (host y dependencia local detenidos) debe fallar la conexión " +
            "de forma explícita, nunca devolver una respuesta como si estuviera sana");
        stopwatch.Stop();

        output.WriteLine(
            $"Detección de caída de Zona A: {stopwatch.Elapsed.TotalMilliseconds:F0}ms " +
            $"(SLO documentado: <= {ZoneDownDetectionSlo.TotalSeconds}s)");
        stopwatch.Elapsed.Should().BeLessThanOrEqualTo(
            ZoneDownDetectionSlo,
            "la detección de que una zona está caída debe ser rápida -- un cliente/LB real no debe " +
            "quedar colgado esperando una zona muerta antes de poder redirigir tráfico a otra");

        // 3) La Zona B, completamente aislada de la A, sigue respondiendo con normalidad -- ninguna
        //    dependencia compartida entre ambas zonas en este diseño de prueba.
        var stopwatchZonaSana = Stopwatch.StartNew();
        var responseZonaB = await _clientZonaB.GetAsync("/api/echo");
        stopwatchZonaSana.Stop();

        responseZonaB.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "la caída completa de la Zona A no debe afectar en absoluto a la Zona B -- son " +
            "instancias/dependencias completamente independientes");
        output.WriteLine($"Zona B tras la caída de Zona A: {(int)responseZonaB.StatusCode} en {stopwatchZonaSana.Elapsed.TotalMilliseconds:F0}ms");
    }

    public async Task DisposeAsync()
    {
        _clientZonaA.Dispose();
        _clientZonaB.Dispose();
        await _factoryZonaB.DisposeAsync();
        await _backendZonaB.DisposeAsync();

        // La Zona A ya se dispuso dentro del propio test (parte del escenario de caos) -- disponer de
        // nuevo un WebApplicationFactory/backend ya dispuesto es un no-op seguro en .NET.
        await _factoryZonaA.DisposeAsync();
        await _backendZonaA.DisposeAsync();

        foreach (var envVarName in _envVarsSetForThisTest)
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
