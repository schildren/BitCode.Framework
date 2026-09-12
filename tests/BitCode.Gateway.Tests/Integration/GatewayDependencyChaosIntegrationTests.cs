using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit.Abstractions;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// F5-13 (Fase 5 — Disaster Recovery y multi-región, <c>docs/chaos-regional-fase5.md</c>), escenario 1
/// "Caída de una dependencia": mide, contra un Redis REAL (Testcontainers, no un mock/fake) detenido a
/// mitad de la prueba, el comportamiento observado del rate limiting distribuido del Gateway
/// (<see cref="BitCode.Gateway.RateLimiting.RedisFixedWindowRateLimiter"/>, F4-08) cuando su única
/// dependencia externa desaparece por completo mientras el proceso del Gateway sigue corriendo.
/// </summary>
/// <remarks>
/// A diferencia de <see cref="GatewayDistributedRateLimitingIntegrationTests"/> (que demuestra el caso
/// feliz, Redis disponible), esta prueba es intencionalmente exploratoria: mide el tiempo real que tarda
/// el Gateway en responder tras la caída de Redis y el código de estado devuelto, y lo compara contra el
/// SLO documentado en <c>docs/chaos-regional-fase5.md</c> ("respuesta controlada y acotada, nunca un
/// cuelgue indefinido"). No asume de antemano cuál será el código de estado exacto -- ese es precisamente
/// el hallazgo que esta prueba deja documentado con evidencia real.
/// </remarks>
[Collection(GatewayEnvironmentVariableCollection.Name)]
public class GatewayDependencyChaosIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string SecretKey = "clave-de-pruebas-de-caos-de-dependencia-32-caracteres-o-mas";
    private const string Issuer = "BitCode.Gateway.Tests";
    private const string Audience = "BitCode.Gateway.Tests.Clients";

    // SLO acotado documentado en docs/chaos-regional-fase5.md, escenario 1: una vez que Redis (L2 del
    // rate limiter distribuido) está totalmente caído, el Gateway debe seguir respondiendo (con éxito o
    // con un error controlado) en, como máximo, este tiempo -- nunca un cuelgue indefinido que requiera
    // intervención manual para liberar la conexión del cliente.
    private static readonly TimeSpan BoundedResponseSlo = TimeSpan.FromSeconds(15);

    private RedisContainer _redisContainer = null!;
    private GatewayTestBackend _backend = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private readonly List<string> _envVarsSetForThisTest = [];

    public async Task InitializeAsync()
    {
        _redisContainer = new RedisBuilder().Build();
        await _redisContainer.StartAsync();

        _backend = new GatewayTestBackend();
        await _backend.StartAsync();

        SetEnvironmentVariable("Jwt__SecretKey", SecretKey);
        SetEnvironmentVariable("Jwt__Issuer", Issuer);
        SetEnvironmentVariable("Jwt__Audience", Audience);
        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
            _backend.BaseAddress);
        SetEnvironmentVariable("OpenTelemetry__ServiceName", $"gateway-dep-chaos-{Guid.NewGuid():N}");
        SetEnvironmentVariable("RateLimiting__PermitLimit", "1000");
        SetEnvironmentVariable("RateLimiting__WindowSeconds", "60");
        SetEnvironmentVariable("RateLimiting__QueueLimit", "0");
        // Rate limiting respaldado por Redis (F4-08) -- la dependencia cuya caída simula este escenario.
        SetEnvironmentVariable("Caching__RedisConnectionString", _redisContainer.GetConnectionString());

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
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
        var user = new ApplicationUser { UserName = "gateway-dep-chaos-user", Email = "gateway-dep-chaos-user@test.com" };

        return generator.GenerateAccessToken(user, roles: [], extraClaims: []);
    }

    [Fact]
    public async Task RedisCaidoPorCompleto_GatewaySigueRespondiendoDeFormaAcotada_SinColgarseIndefinidamente()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        // 1) Con Redis real arriba: confirma el camino feliz -- el rate limiter distribuido funciona
        //    (mismo criterio ya verificado por GatewayDistributedRateLimitingIntegrationTests).
        var responseConRedisArriba = await _client.GetAsync("/api/echo");
        responseConRedisArriba.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2) Caída TOTAL de la dependencia -- se detiene el contenedor real de Redis, no se simula con
        //    un mock ni se apaga solo la conectividad de red parcialmente.
        await _redisContainer.StopAsync();

        // 3) Comportamiento observado tras la caída: se mide el tiempo real de respuesta y el código de
        //    estado devuelto por el Gateway para varios requests consecutivos (para no depender de una
        //    única medición potencialmente no representativa).
        var observaciones = new List<(HttpStatusCode? StatusCode, TimeSpan Elapsed, string? Excepcion)>();

        for (var intento = 1; intento <= 3; intento++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await _client.GetAsync("/api/echo");
                stopwatch.Stop();
                observaciones.Add((response.StatusCode, stopwatch.Elapsed, null));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                observaciones.Add((null, stopwatch.Elapsed, ex.GetType().Name));
            }
        }

        var evidencia = string.Join(
            Environment.NewLine,
            observaciones.Select((o, i) =>
                $"Intento {i + 1}: status={(o.StatusCode is { } sc ? (int)sc : "N/A")}, " +
                $"excepcion={o.Excepcion ?? "ninguna"}, elapsed={o.Elapsed.TotalMilliseconds:F0}ms"));
        output.WriteLine(evidencia);
        output.WriteLine(
            $"SLO documentado (docs/chaos-regional-fase5.md, escenario 1): respuesta acotada en <= " +
            $"{BoundedResponseSlo.TotalSeconds}s por intento, sin cuelgue indefinido.");

        observaciones.Should().OnlyContain(
            o => o.Elapsed <= BoundedResponseSlo,
            "cada intento -- sea que termine en éxito, en un error HTTP controlado, o en una excepción " +
            "de cliente -- debe resolverse dentro del SLO acotado documentado; un cuelgue indefinido " +
            "(sin resolución hasta que el propio test time out) sería la violación real de F5-13" +
            Environment.NewLine + evidencia);

        // El Gateway (proceso .NET) nunca debe terminar/crashear por la caída de Redis -- sigue vivo y
        // el endpoint operativo /health/live, que NO depende de ninguna dependencia externa (F1-25),
        // sigue respondiendo con normalidad durante y después del incidente.
        var liveness = await _client.GetAsync("/health/live");
        liveness.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "el liveness del proceso del Gateway nunca debe depender de que Redis (una dependencia " +
            "externa) esté disponible -- F1-25/F4-08" + Environment.NewLine + evidencia);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _backend.DisposeAsync();
        await _redisContainer.DisposeAsync();

        foreach (var envVarName in _envVarsSetForThisTest)
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
