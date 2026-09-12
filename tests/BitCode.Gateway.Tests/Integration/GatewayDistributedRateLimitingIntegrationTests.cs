using System.Net;
using System.Net.Http.Headers;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// Cierre del pendiente de F4-08 "rate limiting distribuido entre réplicas": DOS instancias REALES del
/// Gateway (dos <see cref="WebApplicationFactory{Program}"/> independientes -- cada una con su propio
/// host/DI, exactamente como dos pods distintos de un mismo Deployment) contra el MISMO Redis real
/// (Testcontainers, <see cref="RedisContainerFixture"/>) deben respetar el límite de forma AGREGADA
/// entre ambas, no cada una por separado. Esta es la evidencia central que distingue el limiter
/// distribuido (<c>RedisFixedWindowRateLimiter</c>) del fallback en memoria nativo de ASP.NET Core
/// (limitación conocida y documentada antes de esta tarea: con N réplicas sin backend compartido, el
/// límite efectivo era N * PermitLimit).
/// </summary>
[Collection(RedisCollection.Name)]
public class GatewayDistributedRateLimitingIntegrationTests(RedisContainerFixture redisFixture) : IAsyncLifetime
{
    private const string SecretKey = "clave-de-pruebas-de-rate-limiting-distribuido-32-caracteres";
    private const string Issuer = "BitCode.Gateway.Tests";
    private const string Audience = "BitCode.Gateway.Tests.Clients";

    // PermitLimit angosto para que la ráfaga del test sea pequeña y determinística. WindowSeconds
    // deliberadamente amplio (60s) para que la ventana no rote a mitad del test.
    private const int PermitLimit = 4;

    private GatewayTestBackend _backend = null!;

    // Prefijo de clave único por instancia de test (GatewayRateLimitingServiceCollectionExtensions
    // incorpora "OpenTelemetry:ServiceName" a la clave de Redis) -- evita que dos ejecuciones de este
    // mismo test (o del test complementario "sin Redis" corriendo en paralelo) que caigan dentro de la
    // MISMA ventana de 60s de reloj de pared compartan el mismo contador en el Redis real y se
    // contaminen entre sí.
    private readonly string _serviceName = $"gateway-rl-test-{Guid.NewGuid():N}";

    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableNamesByConfigKey = new Dictionary<string, string>
    {
        ["OpenTelemetry:ServiceName"] = "OpenTelemetry__ServiceName",
        ["Jwt:SecretKey"] = "Jwt__SecretKey",
        ["Jwt:Issuer"] = "Jwt__Issuer",
        ["Jwt:Audience"] = "Jwt__Audience",
        ["ReverseProxy:Clusters:sample-api-cluster:Destinations:destination1:Address"] =
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
        ["RateLimiting:PermitLimit"] = "RateLimiting__PermitLimit",
        ["RateLimiting:WindowSeconds"] = "RateLimiting__WindowSeconds",
        ["RateLimiting:QueueLimit"] = "RateLimiting__QueueLimit",
        ["Caching:RedisConnectionString"] = "Caching__RedisConnectionString",
    };

    public async Task InitializeAsync()
    {
        _backend = new GatewayTestBackend();
        await _backend.StartAsync();

        SetEnvironmentVariable("OpenTelemetry:ServiceName", _serviceName);
        SetEnvironmentVariable("Jwt:SecretKey", SecretKey);
        SetEnvironmentVariable("Jwt:Issuer", Issuer);
        SetEnvironmentVariable("Jwt:Audience", Audience);
        SetEnvironmentVariable(
            "ReverseProxy:Clusters:sample-api-cluster:Destinations:destination1:Address",
            _backend.BaseAddress);
        SetEnvironmentVariable("RateLimiting:PermitLimit", PermitLimit.ToString());
        SetEnvironmentVariable("RateLimiting:WindowSeconds", "60");
        SetEnvironmentVariable("RateLimiting:QueueLimit", "0");
        SetEnvironmentVariable("Caching:RedisConnectionString", redisFixture.ConnectionString);
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
        var user = new ApplicationUser { UserName = "gateway-rl-test-user", Email = "gateway-rl-test-user@test.com" };

        return generator.GenerateAccessToken(user, roles: [], extraClaims: []);
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        return client;
    }

    [Fact]
    public async Task DosInstanciasDelGateway_ConRedisConfigurado_RespetanElLimiteDeFormaAgregadaEntreAmbas()
    {
        // Dos WebApplicationFactory<Program> INDEPENDIENTES -- dos hosts/DI/PartitionedRateLimiter
        // distintos, exactamente lo que simula dos réplicas/pods reales del mismo Deployment del
        // Gateway. Ambas leen la MISMA configuración de proceso (variables de entorno seteadas en
        // InitializeAsync) -- mismo Caching:RedisConnectionString, así que ambas apuntan al mismo Redis
        // real.
        await using var factoryA = new WebApplicationFactory<Program>();
        await using var factoryB = new WebApplicationFactory<Program>();
        using var clientA = CreateAuthenticatedClient(factoryA);
        using var clientB = CreateAuthenticatedClient(factoryB);

        // PermitLimit = 4 (ver InitializeAsync). Se reparten 2 requests a cada instancia -- ninguna
        // agota su "propia" cuota si el contador fuera en memoria por instancia (2 < 4), pero la SUMA
        // ya llega al límite agregado real (2 + 2 = 4).
        var responses = new List<HttpResponseMessage>
        {
            await clientA.GetAsync("/api/echo"),
            await clientB.GetAsync("/api/echo"),
            await clientA.GetAsync("/api/echo"),
            await clientB.GetAsync("/api/echo"),
        };

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);

        // El quinto request, contra CUALQUIERA de las dos instancias, ya supera el límite agregado de 4.
        // Se manda contra la instancia A -- que hasta acá solo lleva 2 requests PROPIOS: si el contador
        // fuera en memoria por instancia (el comportamiento previo a esta tarea, sin Redis), A lo
        // aceptaría con 200 (2 de 4 propios). Con el contador compartido en Redis, A ve que el total
        // agregado (A + B) ya es 4 y rechaza con 429 -- la evidencia central de que el límite es
        // realmente agregado entre réplicas, no multiplicado por la cantidad de réplicas.
        var fifthOnInstanceA = await clientA.GetAsync("/api/echo");
        fifthOnInstanceA.StatusCode.Should().Be((HttpStatusCode)429);

        // Confirma también desde la otra instancia -- el contador compartido rechaza sin importar por
        // cuál de las dos réplicas entre el request.
        var sixthOnInstanceB = await clientB.GetAsync("/api/echo");
        sixthOnInstanceB.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task DosInstanciasDelGateway_SinRedisConfigurado_CadaUnaAplicaSuPropioLimiteEnMemoria()
    {
        // Control/contraste: sin Caching:RedisConnectionString, el Gateway cae al fallback en memoria
        // (documentado, comportamiento previo a esta tarea) -- cada instancia cuenta SOLO lo que ella
        // misma recibe. Confirma que el fallback sigue funcionando (no rompe el caso sin Redis) y deja
        // en evidencia, por contraste directo con el test de arriba, la limitación conocida que motivó
        // esta tarea: con N réplicas sin backend compartido, el límite efectivo es N * PermitLimit.
        SetEnvironmentVariable("Caching:RedisConnectionString", string.Empty);

        await using var factoryA = new WebApplicationFactory<Program>();
        await using var factoryB = new WebApplicationFactory<Program>();
        using var clientA = CreateAuthenticatedClient(factoryA);
        using var clientB = CreateAuthenticatedClient(factoryB);

        // Cada instancia agota su propia cuota completa (4 de 4) de forma independiente -- 8 requests
        // exitosos en total entre ambas, el doble del PermitLimit configurado.
        for (var i = 0; i < PermitLimit; i++)
        {
            (await clientA.GetAsync("/api/echo")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await clientB.GetAsync("/api/echo")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // El request número 5 en CADA instancia, recién ahí, se rechaza -- cada una respeta el límite
        // solo dentro de sí misma, nunca de forma agregada.
        (await clientA.GetAsync("/api/echo")).StatusCode.Should().Be((HttpStatusCode)429);
        (await clientB.GetAsync("/api/echo")).StatusCode.Should().Be((HttpStatusCode)429);
    }

    public async Task DisposeAsync()
    {
        await _backend.DisposeAsync();
        ClearEnvironmentVariables();
    }
}
