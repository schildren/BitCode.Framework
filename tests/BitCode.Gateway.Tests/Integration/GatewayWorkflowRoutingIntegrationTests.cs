using System.Net;
using System.Net.Http.Headers;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// F9-06 (Fase 9, "Routing", <c>docs/guia-workflow.md</c> sección "Routing (F9-06)"): el Gateway (F4-08,
/// YARP) enruta tráfico HTTP real hacia el host independiente de Workflow (F9-05,
/// <c>samples/Sample.Workflow.Api</c>, representado acá por <see cref="GatewayWorkflowRoutingTestBackend"/>,
/// un backend Kestrel real -- mismo patrón que <see cref="GatewayIntegrationTests"/> con
/// <see cref="GatewayTestBackend"/>) SIN dejar de exigir el mismo auth boundary de F4-08 que ya aplica a
/// todas las demás rutas, y sin romper la ruta pre-existente hacia <c>sample-api</c>. El criterio de
/// aceptación "Cambio reversible" se verifica contra el Gateway real, no en esta clase -- ver
/// <c>docs/guia-workflow.md</c>, sección "Routing (F9-06)".
/// </summary>
[Collection(GatewayEnvironmentVariableCollection.Name)]
public class GatewayWorkflowRoutingIntegrationTests : IAsyncLifetime
{
    private const string SecretKey = "clave-de-pruebas-de-routing-de-workflow-del-gateway-32-caracteres";
    private const string Issuer = "BitCode.Gateway.Tests";
    private const string Audience = "BitCode.Gateway.Tests.Clients";

    private GatewayTestBackend _sampleApiBackend = null!;
    private GatewayWorkflowRoutingTestBackend _workflowBackend = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private static readonly IReadOnlyDictionary<string, string> EnvironmentVariableNamesByConfigKey = new Dictionary<string, string>
    {
        ["Jwt:SecretKey"] = "Jwt__SecretKey",
        ["Jwt:Issuer"] = "Jwt__Issuer",
        ["Jwt:Audience"] = "Jwt__Audience",
        ["ReverseProxy:Clusters:sample-api-cluster:Destinations:destination1:Address"] =
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
        ["ReverseProxy:Clusters:sample-workflow-api-cluster:Destinations:destination1:Address"] =
            "ReverseProxy__Clusters__sample-workflow-api-cluster__Destinations__destination1__Address",
    };

    public async Task InitializeAsync()
    {
        _sampleApiBackend = new GatewayTestBackend();
        await _sampleApiBackend.StartAsync();

        _workflowBackend = new GatewayWorkflowRoutingTestBackend();
        await _workflowBackend.StartAsync();

        SetEnvironmentVariable("Jwt:SecretKey", SecretKey);
        SetEnvironmentVariable("Jwt:Issuer", Issuer);
        SetEnvironmentVariable("Jwt:Audience", Audience);
        SetEnvironmentVariable(
            "ReverseProxy:Clusters:sample-api-cluster:Destinations:destination1:Address",
            _sampleApiBackend.BaseAddress);
        SetEnvironmentVariable(
            "ReverseProxy:Clusters:sample-workflow-api-cluster:Destinations:destination1:Address",
            _workflowBackend.BaseAddress);

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
        var user = new ApplicationUser { UserName = "gateway-workflow-test-user", Email = "gateway-workflow-test-user@test.com" };

        return generator.GenerateAccessToken(user, roles: [], extraClaims: []);
    }

    [Fact]
    public async Task Proxy_SinToken_RechazaRutaDeWorkflow401AntesDeLlegarAlBackend()
    {
        var response = await _client.GetAsync("/api/v1/workflows/");

        // Mismo auth boundary de F4-08 (RequireAuthorization en MapReverseProxy, sin excepción por
        // ruta) -- la ruta nueva no abre un bypass de autenticación.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Proxy_ConTokenValido_RuteaHaciaElHostIndependienteDeWorkflow_NoHaciaSampleApi()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        var response = await _client.GetAsync("/api/v1/workflows/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("sample-workflow-api");
    }

    [Fact]
    public async Task Proxy_ConTokenValido_LaRutaExistenteDeSampleApiSigueFuncionandoSinRegresion()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        // Ruta pre-existente (F4-08) -- agregar el cluster/ruta de Workflow no debe alterar en nada el
        // comportamiento de la ruta "catch-all" original hacia sample-api.
        var response = await _client.GetAsync("/api/echo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // NOTA sobre reversibilidad (criterio de aceptación de F9-06): la reversión real ("apagar" la ruta
    // hacia Workflow y volver a que ese path lo resuelva el destino original) se verificó contra el
    // Gateway real (proceso .NET real vía `dotnet run`, no `WebApplicationFactory`), editando
    // "ReverseProxy:Routes:sample-workflow-api" en el `appsettings.json` efectivamente cargado y
    // reiniciando el proceso -- ver `docs/guia-workflow.md`, sección "Routing (F9-06)", para la
    // evidencia completa (incluye la confirmación de que este framework NO recarga la configuración de
    // YARP en caliente). No se automatizó una segunda instancia de `WebApplicationFactory<Program>`
    // dentro de esta clase de pruebas para simular esa misma reversión: al construir un SEGUNDO host de
    // prueba en el mismo proceso xUnit, la ruta reconfigurada dejaba de coincidir con cualquier request
    // (404) de una forma no reproducible de forma confiable (comportamiento no observado contra el
    // Gateway real) -- se prefirió no dejar una prueba automatizada frágil/engañosa en vez de forzarla.

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _sampleApiBackend.DisposeAsync();
        await _workflowBackend.DisposeAsync();
        ClearEnvironmentVariables();
    }
}
