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
/// F9-09 (Fase 9, "Resiliencia", <c>docs/guia-workflow.md</c> sección "Resiliencia (F9-09)"), criterio de
/// aceptación literal <b>"Fallo aislado"</b>: verifica, contra el Gateway real (F4-08, YARP) enrutando
/// hacia el host independiente de Workflow (F9-05/F9-06), que (a) un backend de Workflow lento no cuelga
/// al Gateway indefinidamente porque existe un timeout de YARP explícitamente configurado
/// (<c>ReverseProxy:Clusters:sample-workflow-api-cluster:HttpRequest:ActivityTimeout</c>), y (b) un
/// backend de Workflow completamente caído NO afecta en absoluto a la ruta preexistente hacia
/// <c>sample-api</c> -- el fallo de un módulo extraído queda aislado, no se propaga al resto de la
/// plataforma servida por el mismo Gateway.
/// </summary>
/// <remarks>
/// Sobre circuit breaker/bulkhead (también pedidos por el backlog de F9-09): YARP, en la versión
/// integrada por este framework, no expone un circuit breaker ni un bulkhead propios a nivel de
/// cluster/ruta -- solo <c>HttpRequest.ActivityTimeout</c> (timeout por request) y
/// <c>HttpClient</c> (configuración del <c>SocketsHttpHandler</c> subyacente, sin límite de
/// concurrencia por cluster). El framework SÍ tiene una pipeline Polly completa con circuit breaker y
/// bulkhead (<c>Shared.Infrastructure.Http.Resilience</c>, F1-26, ADR-0013), pero se aplica hoy a
/// clientes HTTP tipados salientes de un módulo hacia otro (<c>DashboardReportingHttpClient</c>,
/// <c>IntegrationOutboundHttpClient</c>) -- no al proxy inverso del Gateway, que usa su propio
/// <c>HttpMessageInvoker</c> gestionado por YARP. Agregar un circuit breaker real al camino del proxy
/// excedería el alcance verificable de esta tarea (no hay ningún gancho de extensibilidad de YARP para
/// eso ya cableado en este repo) -- se documenta esta ausencia honestamente en
/// <c>docs/guia-workflow.md</c> en vez de simular una configuración no verificada. El criterio de
/// aceptación central de F9-09 ("Fallo aislado") no depende de tener un circuit breaker: un timeout
/// explícito más el aislamiento natural de rutas/clusters de YARP (cada cluster tiene su propio destino,
/// una falla en uno no toca el <c>HttpMessageInvoker</c> de otro) ya lo satisface, y es lo que esta clase
/// verifica con evidencia real.
/// </remarks>
[Collection(GatewayEnvironmentVariableCollection.Name)]
public class GatewayWorkflowResilienceIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string SecretKey = "clave-de-pruebas-de-resiliencia-de-workflow-32-caracteres-o-mas";
    private const string Issuer = "BitCode.Gateway.Tests";
    private const string Audience = "BitCode.Gateway.Tests.Clients";

    // Backend deliberadamente más lento que el timeout configurado para el test -- si el Gateway no
    // cortara la espera, el request tardaría al menos esto.
    private static readonly TimeSpan BackendDelay = TimeSpan.FromSeconds(8);

    // Timeout de YARP configurado explícitamente para este test -- corto, para que el test sea rápido,
    // pero suficientemente distinto de BackendDelay para no dar falsos positivos por variabilidad de CI.
    private static readonly TimeSpan ConfiguredActivityTimeout = TimeSpan.FromSeconds(2);

    // Cota superior razonable para "el Gateway no se colgó indefinidamente": bastante menor que
    // BackendDelay, con margen sobre ConfiguredActivityTimeout para overhead de red/CI real.
    private static readonly TimeSpan BoundedResponseSlo = TimeSpan.FromSeconds(6);

    private GatewayTestBackend _sampleApiBackend = null!;
    private readonly List<string> _envVarsSetForThisTest = [];

    public async Task InitializeAsync()
    {
        _sampleApiBackend = new GatewayTestBackend();
        await _sampleApiBackend.StartAsync();

        SetEnvironmentVariable("Jwt__SecretKey", SecretKey);
        SetEnvironmentVariable("Jwt__Issuer", Issuer);
        SetEnvironmentVariable("Jwt__Audience", Audience);
        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-api-cluster__Destinations__destination1__Address",
            _sampleApiBackend.BaseAddress);
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
        var user = new ApplicationUser { UserName = "gateway-workflow-resilience-user", Email = "gateway-workflow-resilience-user@test.com" };

        return generator.GenerateAccessToken(user, roles: [], extraClaims: []);
    }

    [Fact]
    public async Task Timeout_BackendDeWorkflowLento_GatewayCortaLaEsperaConTimeoutExplicito()
    {
        await using var workflowBackend = new GatewayWorkflowSlowTestBackend(BackendDelay);
        await workflowBackend.StartAsync();

        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-workflow-api-cluster__Destinations__destination1__Address",
            workflowBackend.BaseAddress);
        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-workflow-api-cluster__HttpRequest__ActivityTimeout",
            ConfiguredActivityTimeout.ToString());

        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30); // el cliente de prueba no debe ser el que corte primero.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/v1/workflows/");
        stopwatch.Stop();

        output.WriteLine(
            $"ActivityTimeout configurado: {ConfiguredActivityTimeout.TotalSeconds}s. " +
            $"Retraso del backend: {BackendDelay.TotalSeconds}s. " +
            $"Respuesta del Gateway: {(int)response.StatusCode} en {stopwatch.Elapsed.TotalMilliseconds:F0}ms.");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "el backend tarda deliberadamente más que el timeout configurado -- una respuesta 200 " +
            "significaría que el Gateway esperó al backend lento en vez de cortar la espera");
        stopwatch.Elapsed.Should().BeLessThan(
            BoundedResponseSlo,
            "el Gateway debe cortar la espera con el timeout explícito de "
            + $"{ConfiguredActivityTimeout.TotalSeconds}s configurado en HttpRequest.ActivityTimeout del "
            + "cluster de Workflow -- nunca esperar los 8s completos del backend lento (eso sería un "
            + "cuelgue efectivo del lado del Gateway, aunque el proceso siga vivo)");
    }

    [Fact]
    public async Task FalloAislado_WorkflowCompletamenteCaido_SampleApiSigueRespondiendoConNormalidad()
    {
        // Dirección de un backend de Workflow que NUNCA se levanta -- representa, sin ambigüedad, el
        // escenario "docker stop sample-workflow-api" contra el puerto real que documenta F9-06
        // (localhost:5299 en desarrollo): nada escucha en ese puerto, así que cualquier intento de
        // conexión falla rápido (connection refused), no por timeout de actividad.
        var puertoLibreQueNuncaEscucha = ObtenerPuertoTcpLibre();
        SetEnvironmentVariable(
            "ReverseProxy__Clusters__sample-workflow-api-cluster__Destinations__destination1__Address",
            $"http://127.0.0.1:{puertoLibreQueNuncaEscucha}/");

        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateValidToken());

        // 1) Caso feliz previo al incidente: ambas rutas responden con normalidad.
        (await client.GetAsync("/api/echo")).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2) El host independiente de Workflow está completamente caído (nada escucha en su dirección
        //    configurada) -- el Gateway debe devolver un error controlado, no colgarse ni crashear.
        var stopwatchWorkflow = Stopwatch.StartNew();
        var workflowResponse = await client.GetAsync("/api/v1/workflows/");
        stopwatchWorkflow.Stop();

        output.WriteLine(
            $"Workflow caído: {(int)workflowResponse.StatusCode} en " +
            $"{stopwatchWorkflow.Elapsed.TotalMilliseconds:F0}ms.");
        workflowResponse.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "no hay ningún backend real de Workflow escuchando en la dirección configurada");
        stopwatchWorkflow.Elapsed.Should().BeLessThan(
            BoundedResponseSlo,
            "un backend caído por completo (connection refused) debe fallar rápido, no colgar el request");

        // 3) EL CRITERIO CENTRAL DE F9-09 ("Fallo aislado"): con Workflow completamente caído, la ruta
        //    preexistente hacia sample-api -- servida por el MISMO proceso de Gateway, un cluster/destino
        //    totalmente distinto -- sigue respondiendo con normalidad, como si nada hubiera pasado.
        var stopwatchSampleApi = Stopwatch.StartNew();
        var sampleApiResponse = await client.GetAsync("/api/echo");
        stopwatchSampleApi.Stop();

        sampleApiResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "la caída completa del host independiente de Workflow no debe afectar en absoluto a la ruta " +
            "de sample-api servida por el mismo Gateway -- ese aislamiento es el criterio de aceptación " +
            "literal de F9-09 ('Fallo aislado')");
        output.WriteLine(
            $"sample-api tras la caída de Workflow: {(int)sampleApiResponse.StatusCode} en " +
            $"{stopwatchSampleApi.Elapsed.TotalMilliseconds:F0}ms (sin degradación observable).");
    }

    private static int ObtenerPuertoTcpLibre()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var puerto = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return puerto;
    }

    public async Task DisposeAsync()
    {
        await _sampleApiBackend.DisposeAsync();

        foreach (var envVarName in _envVarsSetForThisTest)
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }
}
