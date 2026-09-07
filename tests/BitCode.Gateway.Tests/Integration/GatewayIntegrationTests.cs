using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// F4-08 -- criterio de aceptación "Overhead dentro del target" verificado cualitativamente (F4-14
/// hace el benchmark formal de carga) y los cuatro pilares del backlog: routing, auth boundary, rate
/// limits y headers, todos contra el Gateway real (Kestrel/TestServer) proxyando a un backend HTTP
/// real (<see cref="GatewayTestBackend"/>), nunca mockeado.
/// </summary>
public class GatewayIntegrationTests : IAsyncLifetime
{
    private const string SecretKey = "clave-de-pruebas-de-integracion-del-gateway-32-caracteres";
    private const string Issuer = "BitCode.Gateway.Tests";
    private const string Audience = "BitCode.Gateway.Tests.Clients";

    private GatewayTestBackend _backend = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableNamesByConfigKey = new Dictionary<string, string>
    {
        ["Jwt:SecretKey"] = "Jwt__SecretKey",
        ["Jwt:Issuer"] = "Jwt__Issuer",
        ["Jwt:Audience"] = "Jwt__Audience",
        ["ReverseProxy:Clusters:sample-api-cluster:Destinations:destination1:Address"] =
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
        ["RateLimiting:PermitLimit"] = "RateLimiting__PermitLimit",
        ["RateLimiting:WindowSeconds"] = "RateLimiting__WindowSeconds",
        ["RateLimiting:QueueLimit"] = "RateLimiting__QueueLimit",
        ["RequestLimits:MaxRequestBodySizeBytes"] = "RequestLimits__MaxRequestBodySizeBytes",
    };

    public async Task InitializeAsync()
    {
        _backend = new GatewayTestBackend();
        await _backend.StartAsync();

        // Program.cs (top-level statements) lee "Jwt"/"ReverseProxy" de forma EAGER al registrar
        // AddSharedJwtBearerAuthentication/AddGatewayRateLimiting -- antes de que
        // WebApplicationFactory.ConfigureAppConfiguration llegue a aportar sus fuentes adicionales
        // (que se agregan recién al interceptar el Build() real del host, ya tarde para una lectura
        // eager). Variables de entorno, en cambio, ya forman parte de la configuración por defecto de
        // WebApplication.CreateBuilder(args) desde el arranque -- mismo mecanismo que usa
        // HealthCheckEndpointsIntegrationTests (samples/Sample.Api.Tests) para ConnectionStrings__Default.
        SetEnvironmentVariable("Jwt:SecretKey", SecretKey);
        SetEnvironmentVariable("Jwt:Issuer", Issuer);
        SetEnvironmentVariable("Jwt:Audience", Audience);
        SetEnvironmentVariable(
            "ReverseProxy:Clusters:sample-api-cluster:Destinations:destination1:Address",
            _backend.BaseAddress);
        // Ventana angosta y a propósito para que el test de rate limiting (más abajo) no dependa de
        // esperar minutos ni de enviar miles de requests.
        SetEnvironmentVariable("RateLimiting:PermitLimit", "2");
        SetEnvironmentVariable("RateLimiting:WindowSeconds", "30");
        SetEnvironmentVariable("RateLimiting:QueueLimit", "0");
        // Límite angosto y a propósito (1 KiB) para que el test de tamaño de body (más abajo) no
        // dependa de enviar un payload real de varios MiB.
        SetEnvironmentVariable("RequestLimits:MaxRequestBodySizeBytes", "1024");

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    private static void SetEnvironmentVariable(string configKey, string value) =>
        Environment.SetEnvironmentVariable(EnvironmentVariableNamesByConfigKey[configKey], value);

    private static void ClearEnvironmentVariables()
    {
        foreach (var environmentVariableName in EnvironmentVariableNamesByConfigKey.Values)
        {
            Environment.SetEnvironmentVariable(environmentVariableName, null);
        }
    }

    private static string GenerateValidToken()
    {
        var options = Options.Create(new JwtOptions
        {
            SecretKey = SecretKey,
            Issuer = Issuer,
            Audience = Audience,
        });
        var generator = new JwtTokenGenerator(options);
        var user = new ApplicationUser { UserName = "gateway-test-user", Email = "gateway-test-user@test.com" };

        return generator.GenerateAccessToken(user, roles: [], extraClaims: []);
    }

    [Fact]
    public async Task Proxy_SinToken_Rechaza401AntesDeLlegarAlBackend()
    {
        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Proxy_ConTokenInvalido_Rechaza401()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");

        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Proxy_ConTokenValido_RuteaAlBackendReal()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Proxy_ConTokenValido_PropagaHeadersXForwardedHaciaElBackend()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        var response = await _client.GetAsync("/api/echo");
        var body = await response.Content.ReadAsStringAsync();
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(body)!;

        // YARP agrega estos headers por defecto al proxyar (F4-08, "Headers": forwarding estándar) --
        // el backend real los recibe sin que el Gateway los declare explícitamente por ruta.
        // X-Forwarded-For queda fuera de esta aserción a propósito: YARP solo lo agrega cuando
        // HttpContext.Connection.RemoteIpAddress no es null, y TestServer (WebApplicationFactory, sin
        // socket TCP real de por medio) nunca lo popula -- comportamiento propio del host de pruebas,
        // no del Gateway (en un Kestrel real, detrás de un socket TCP real, sí está siempre presente).
        headers.Should().ContainKey("X-Forwarded-Proto");
        headers.Should().ContainKey("X-Forwarded-Host");
    }

    [Fact]
    public async Task Proxy_ConTokenValido_RemueveHeadersInternosSensiblesAntesDeReenviar()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());
        _client.DefaultRequestHeaders.Add("X-Internal-Api-Key", "no-deberia-llegar-al-backend");

        var response = await _client.GetAsync("/api/echo");
        var body = await response.Content.ReadAsStringAsync();
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(body)!;

        headers.Should().NotContainKey("X-Internal-Api-Key");
    }

    [Fact]
    public async Task Proxy_ConTokenValido_SuperarLimiteDeRateLimiting_Rechaza429()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        // PermitLimit configurado a 2 (ver InitializeAsync) -- las primeras dos pasan, la tercera
        // dentro de la misma ventana se rechaza.
        var first = await _client.GetAsync("/api/echo");
        var second = await _client.GetAsync("/api/echo");
        var third = await _client.GetAsync("/api/echo");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task Proxy_ConBodyQueSuperaElLimiteConfigurado_Rechaza413AntesDeAutenticarNiProxyar()
    {
        // Deliberadamente SIN token: el límite de tamaño de body (F4-09) se evalúa ANTES de auth en
        // Program.cs -- si ese orden se rompiera, este request fallaría con 401 en vez de 413.
        var oversizedBody = new string('a', 2048); // supera el límite de 1024 bytes configurado en InitializeAsync
        var content = new StringContent(oversizedBody, Encoding.UTF8, "text/plain");
        content.Headers.ContentLength = oversizedBody.Length;

        var response = await _client.PostAsync("/api/echo", content);

        response.StatusCode.Should().Be((HttpStatusCode)413);
    }

    [Fact]
    public async Task Proxy_ConBodyDentroDelLimiteConfigurado_NoLoRechazaPorTamano()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());
        var smallBody = new string('a', 100);
        var content = new StringContent(smallBody, Encoding.UTF8, "text/plain");

        var response = await _client.PostAsync("/api/echo", content);

        // El backend de prueba solo mapea GET /api/echo -- lo relevante acá es que NO se rechace por
        // tamaño (413); cualquier otro código de respuesta (p.ej. 404/405 del backend real) confirma
        // que el middleware de límite de tamaño dejó pasar el request.
        response.StatusCode.Should().NotBe((HttpStatusCode)413);
    }

    [Fact]
    public async Task GatewayLiveness_RespondeOkSinPasarPorAuthNiProxy()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _backend.DisposeAsync();
        ClearEnvironmentVariables();
    }
}
