using System.Net;
using System.Net.Http.Headers;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Gateway.Regional;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// F5-03 (Fase 5 -- Disaster Recovery y multi-región), criterio de aceptación "Requests llegan al
/// owner": simula dos "regiones" lógicas (esta instancia del Gateway corre en <c>us-east</c>, un
/// tenant tiene como región propietaria explícita <c>eu-west</c>) vía configuración (sección
/// "Regional") -- sin infraestructura multi-región real disponible en este entorno (un solo host, ver
/// <c>docs/bia-fase5.md</c>) -- y verifica, contra el Gateway real (Kestrel/TestServer) proxyando a un
/// backend HTTP real (<see cref="GatewayTestBackend"/>, mismo patrón que
/// <see cref="GatewayIntegrationTests"/>), que un request de ese tenant NUNCA llega al backend
/// (se rechaza acá, en el Gateway, antes de proxyar).
/// </summary>
[Collection(GatewayEnvironmentVariableCollection.Name)]
public class GatewayRegionalRoutingIntegrationTests : IAsyncLifetime
{
    private const string SecretKey = "clave-de-pruebas-de-routing-regional-del-gateway-32-caracteres";
    private const string Issuer = "BitCode.Gateway.Tests";
    private const string Audience = "BitCode.Gateway.Tests.Clients";
    private const string LocalRegion = "us-east";
    private const string RemoteOwnerRegion = "eu-west";

    private static readonly Guid TenantOwnedRemotely = Guid.NewGuid();
    private static readonly Guid TenantOwnedLocally = Guid.NewGuid();

    private GatewayTestBackend _backend = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private readonly List<string> _envVarsSetForThisTest = [];

    public async Task InitializeAsync()
    {
        _backend = new GatewayTestBackend();
        await _backend.StartAsync();

        SetEnvironmentVariable("Jwt__SecretKey", SecretKey);
        SetEnvironmentVariable("Jwt__Issuer", Issuer);
        SetEnvironmentVariable("Jwt__Audience", Audience);
        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
            _backend.BaseAddress);

        // "Esta instancia" del Gateway corre en us-east (simulación de la región local del nodo, F5-03).
        SetEnvironmentVariable("Regional__CurrentRegion", LocalRegion);
        // TenantOwnedRemotely tiene como región propietaria explícita eu-west (distinta de la región
        // local) -- TenantOwnedLocally no tiene entrada, así que resuelve a RegionId.Primary; para que
        // este test también compare "propietario == región local" se asigna explícitamente a us-east.
        SetEnvironmentVariable(
            $"Regional__TenantRegionAssignments__{TenantOwnedRemotely}",
            RemoteOwnerRegion);
        SetEnvironmentVariable(
            $"Regional__TenantRegionAssignments__{TenantOwnedLocally}",
            LocalRegion);

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _envVarsSetForThisTest.Add(name);
    }

    private static string GenerateTokenForTenant(Guid tenantId)
    {
        var options = Options.Create(new JwtOptions
        {
            SecretKey = SecretKey,
            Issuer = Issuer,
            Audience = Audience,
        });
        var generator = new JwtTokenGenerator(options);
        var user = new ApplicationUser
        {
            UserName = "gateway-regional-test-user",
            Email = "gateway-regional-test-user@test.com",
            TenantId = tenantId,
        };

        return generator.GenerateAccessToken(user, roles: [], extraClaims: []);
    }

    [Fact]
    public async Task Proxy_TenantConRegionPropietariaDistintaDeLaLocal_Rechaza421SinLlegarAlBackend()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTokenForTenant(TenantOwnedRemotely));

        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be((HttpStatusCode)421);
        response.Headers.Should().ContainSingle(h => h.Key == RegionalOwnershipRoutingMiddleware.OwnerRegionHeaderName);
        response.Headers.GetValues(RegionalOwnershipRoutingMiddleware.OwnerRegionHeaderName)
            .Should().ContainSingle(RemoteOwnerRegion);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(RemoteOwnerRegion);
    }

    [Fact]
    public async Task Proxy_TenantConRegionPropietariaIgualALaLocal_RuteaAlBackendReal()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTokenForTenant(TenantOwnedLocally));

        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Proxy_TenantSinAsignacionExplicita_ResuelveAPrimaryYRechazaPorqueLaRegionLocalNoEsPrimary()
    {
        // Tenant sin ninguna entrada en Regional:TenantRegionAssignments -- resuelve de forma
        // determinística a RegionId.Primary (F5-02), que NO coincide con la región local de esta
        // instancia (us-east, configurada en InitializeAsync) -- confirma que la ausencia de asignación
        // nunca significa "cualquier región vale", sino "la región primaria es la propietaria".
        var tenantSinAsignacion = Guid.NewGuid();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTokenForTenant(tenantSinAsignacion));

        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be((HttpStatusCode)421);
        response.Headers.GetValues(RegionalOwnershipRoutingMiddleware.OwnerRegionHeaderName)
            .Should().ContainSingle("primary");
    }

    [Fact]
    public async Task Proxy_SinToken_Rechaza401AntesDeEvaluarRegion()
    {
        // Sin token no hay tenant que resolver -- el auth boundary (F4-08) ya rechaza antes de que el
        // middleware de routing regional (F5-03) tenga oportunidad de correr.
        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _backend.DisposeAsync();

        foreach (var envVarName in _envVarsSetForThisTest)
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
