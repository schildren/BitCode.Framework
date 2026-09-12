using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BitCode.Framework.Platform.Dashboard;
using BitCode.Framework.Platform.Dashboard.Widgets;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Dashboard.Api.Tests.Integration;

/// <summary>
/// Verifica, contra SQL Server real (Testcontainers) y un servidor HTTP real embebido en el mismo proceso
/// (<see cref="TestReportingHttpServer"/>, Kestrel real -- NO un mock de <c>HttpClient</c>) que simula la
/// API pública de Reporting (Fase 6, módulo 11), el módulo Dashboard (Fase 6, módulo 12 -- el ÚLTIMO):
/// widgets, resolución de métrica vía HTTP con clasificación de fallos, y ownership estricto de las
/// preferencias de cada usuario.
/// </summary>
public class DashboardEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private readonly TestReportingHttpServer _reportingServer = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly IReadOnlyList<string> TodosLosPermisos =
    [
        DashboardPermissions.WidgetsVer,
        DashboardPermissions.WidgetsAdministrar,
    ];

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sqlServerFixture.InitializeAsync(), _reportingServer.StartAsync());

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleDashboardApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleDashboardApiTestsIdentity", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("Dashboard__ReportingBaseUrl", _reportingServer.BaseUrl);
        // Backoff/timeout mínimos para que los tests que fuerzan un fallo transitorio de "Reporting" no
        // esperen los defaults de producción (F1-26: 3 reintentos, 1s de base) -- mismo criterio que
        // IntegrationHubEndpointsIntegrationTests.
        Environment.SetEnvironmentVariable("Dashboard__HttpResilience__RetryBaseDelay", "00:00:00.0100000");
        Environment.SetEnvironmentVariable("Dashboard__HttpResilience__AttemptTimeout", "00:00:02");
        Environment.SetEnvironmentVariable("Dashboard__HttpResilience__TotalTimeout", "00:00:05");

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await Task.WhenAll(_sqlServerFixture.DisposeAsync(), _reportingServer.DisposeAsync().AsTask());
    }

    private async Task<string> SeedActorAsync(string userName, IReadOnlyList<string>? permissions = null, Guid? tenantId = null)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var roleName = $"rol-{userName}";
        var role = new ApplicationRole(roleName);
        (await roleManager.CreateAsync(role)).Succeeded.Should().BeTrue();

        foreach (var permission in permissions ?? [])
        {
            (await roleManager.AddPermissionAsync(role, permission)).Succeeded.Should().BeTrue();
        }

        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.local", TenantId = tenantId ?? TenantId };
        (await userManager.CreateAsync(user, "Contraseña!Segura1")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();

        return tokenGenerator.GenerateAccessToken(user, [roleName], []);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private HttpRequestMessage BuildRequestWithBody(HttpMethod method, string url, string token, object body)
    {
        var request = BuildRequest(method, url, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<Guid> AgregarWidgetAsync(
        string token, TipoWidgetDashboard tipo = TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion,
        string titulo = "Duración promedio", Guid? workflowDefinitionId = null)
    {
        var response = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/dashboard/widgets", token,
            new { Tipo = tipo, Titulo = titulo, WorkflowDefinitionId = workflowDefinitionId ?? Guid.NewGuid() }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    [Fact]
    public async Task ListarWidgets_SinAutenticacion_Retorna401()
    {
        var response = await _client!.GetAsync("/api/v1/dashboard/widgets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AgregarWidget_ConTituloVacio_Retorna400()
    {
        var token = await SeedActorAsync("admin-titulo-vacio", TodosLosPermisos);

        var response = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/dashboard/widgets", token,
            new { Tipo = TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion, Titulo = "", WorkflowDefinitionId = Guid.NewGuid() }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Ejerce el validador real de <c>AgregarWidgetCommandValidator</c> con datos inválidos --
    /// hallazgo Medio real de Import and Export (Fase 6, módulo 10): un validador "declarado pero nunca
    /// probado" no es suficiente evidencia de que el pipeline realmente lo ejecuta.</summary>
    [Fact]
    public async Task AgregarWidget_DeTipoPromedioDuracionSinWorkflowDefinitionId_Retorna400()
    {
        var token = await SeedActorAsync("admin-sin-definicion", TodosLosPermisos);

        var response = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/dashboard/widgets", token,
            new { Tipo = TipoWidgetDashboard.PromedioDuracionWorkflowPorDefinicion, Titulo = "Sin definición", WorkflowDefinitionId = (Guid?)null }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AgregarWidget_CaminoFeliz_ApareceEnElPropioListado()
    {
        var token = await SeedActorAsync("admin-camino-feliz", TodosLosPermisos);

        var widgetId = await AgregarWidgetAsync(token, titulo: "Mi widget");

        var listadoResponse = await _client!.SendAsync(BuildRequest(HttpMethod.Get, "/api/v1/dashboard/widgets", token));
        var listado = await listadoResponse.Content.ReadFromJsonAsync<List<DashboardWidgetResponseDto>>();

        listado.Should().ContainSingle(w => w.Id == widgetId && w.Titulo == "Mi widget");
    }

    /// <summary>Corrige por diseño, desde el primer corte, el hallazgo Alto real de Task Inbox (Fase 6,
    /// módulo 7): un endpoint de DETALLE por id debe repetir el mismo chequeo de ownership que el
    /// listado, no solo el listado.</summary>
    [Fact]
    public async Task ObtenerWidget_DeOtroUsuario_Retorna403()
    {
        var tokenDuenio = await SeedActorAsync("duenio-widget", TodosLosPermisos);
        var tokenIntruso = await SeedActorAsync("intruso-widget", TodosLosPermisos);
        var widgetId = await AgregarWidgetAsync(tokenDuenio);

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/dashboard/widgets/{widgetId}", tokenIntruso));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task QuitarWidget_DeOtroUsuario_Retorna403YNoLoElimina()
    {
        var tokenDuenio = await SeedActorAsync("duenio-quitar", TodosLosPermisos);
        var tokenIntruso = await SeedActorAsync("intruso-quitar", TodosLosPermisos);
        var widgetId = await AgregarWidgetAsync(tokenDuenio);

        var respuestaIntruso = await _client!.SendAsync(
            BuildRequest(HttpMethod.Delete, $"/api/v1/dashboard/widgets/{widgetId}", tokenIntruso));
        respuestaIntruso.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var respuestaDuenio = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/dashboard/widgets/{widgetId}", tokenDuenio));
        respuestaDuenio.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListarWidgets_SoloDevuelveLosPropiosDelActor()
    {
        var tokenUsuarioA = await SeedActorAsync("usuario-a-listado", TodosLosPermisos);
        var tokenUsuarioB = await SeedActorAsync("usuario-b-listado", TodosLosPermisos);
        await AgregarWidgetAsync(tokenUsuarioA, titulo: "Widget de A");
        await AgregarWidgetAsync(tokenUsuarioB, titulo: "Widget de B");

        var listadoA = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, "/api/v1/dashboard/widgets", tokenUsuarioA)))
            .Content.ReadFromJsonAsync<List<DashboardWidgetResponseDto>>();

        listadoA.Should().ContainSingle().Which.Titulo.Should().Be("Widget de A");
    }

    [Fact]
    public async Task ReordenarWidgets_CaminoFeliz_CambiaLaPosicion()
    {
        var token = await SeedActorAsync("admin-reordenar", TodosLosPermisos);
        var primerWidgetId = await AgregarWidgetAsync(token, titulo: "Primero");
        var segundoWidgetId = await AgregarWidgetAsync(token, titulo: "Segundo");

        var reordenarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Put, "/api/v1/dashboard/widgets/orden", token,
            new { WidgetIdsEnOrden = new[] { segundoWidgetId, primerWidgetId } }));
        reordenarResponse.StatusCode.Should().Be(HttpStatusCode.OK, await reordenarResponse.Content.ReadAsStringAsync());

        var listado = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, "/api/v1/dashboard/widgets", token)))
            .Content.ReadFromJsonAsync<List<DashboardWidgetResponseDto>>();

        listado![0].Id.Should().Be(segundoWidgetId);
        listado[0].Orden.Should().Be(0);
        listado[1].Id.Should().Be(primerWidgetId);
        listado[1].Orden.Should().Be(1);
    }

    /// <summary>Ownership también en la mutación masiva: un actor no puede colar el id de un widget ajeno
    /// en su propio pedido de reordenamiento (IDOR).</summary>
    [Fact]
    public async Task ReordenarWidgets_ConIdDeOtroUsuario_Retorna403()
    {
        var tokenDuenio = await SeedActorAsync("duenio-reordenar", TodosLosPermisos);
        var tokenIntruso = await SeedActorAsync("intruso-reordenar", TodosLosPermisos);
        var widgetDelDuenioId = await AgregarWidgetAsync(tokenDuenio);
        var widgetDelIntrusoId = await AgregarWidgetAsync(tokenIntruso);

        var response = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Put, "/api/v1/dashboard/widgets/orden", tokenIntruso,
            new { WidgetIdsEnOrden = new[] { widgetDelIntrusoId, widgetDelDuenioId } }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ObtenerMetrica_DeOtroUsuario_Retorna403()
    {
        var tokenDuenio = await SeedActorAsync("duenio-metrica", TodosLosPermisos);
        var tokenIntruso = await SeedActorAsync("intruso-metrica", TodosLosPermisos);
        var widgetId = await AgregarWidgetAsync(tokenDuenio);

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/dashboard/widgets/{widgetId}/metrica", tokenIntruso));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ObtenerMetrica_CaminoFeliz_LlamaAReportingYDevuelveElPromedioReal()
    {
        var token = await SeedActorAsync("admin-metrica-feliz", TodosLosPermisos);
        var workflowDefinitionId = Guid.NewGuid();
        _reportingServer.ConfigurarDato(workflowDefinitionId, cantidadInstanciasFinalizadas: 7, promedioDuracionSegundos: 123.5);
        var widgetId = await AgregarWidgetAsync(token, workflowDefinitionId: workflowDefinitionId);

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/dashboard/widgets/{widgetId}/metrica", token));
        var metrica = await response.Content.ReadFromJsonAsync<DashboardWidgetMetricaResponseDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        metrica!.Estado.Should().Be(0); // ReportingMetricEstado.Disponible
        metrica.PromedioDuracionSegundos.Should().Be(123.5);
        metrica.CantidadInstanciasFinalizadas.Should().Be(7);
    }

    [Fact]
    public async Task ObtenerMetrica_SinInstanciasFinalizadasTodavia_DevuelveDisponibleSinPromedio()
    {
        var token = await SeedActorAsync("admin-metrica-sin-datos", TodosLosPermisos);
        var widgetId = await AgregarWidgetAsync(token); // ninguna fila configurada en el servidor de prueba

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/dashboard/widgets/{widgetId}/metrica", token));
        var metrica = await response.Content.ReadFromJsonAsync<DashboardWidgetMetricaResponseDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        metrica!.Estado.Should().Be(0); // Disponible -- ausencia de datos no es un fallo
        metrica.PromedioDuracionSegundos.Should().BeNull();
    }

    /// <summary>Corrige por diseño, desde el primer corte, el mismo tipo de hallazgo Alto que ya
    /// corrigieron Notifications/Integration Hub: un fallo del sistema externo (acá, Reporting) nunca debe
    /// escapar como excepción no controlada -- debe resolverse a un estado "no disponible" clasificado.</summary>
    [Fact]
    public async Task ObtenerMetrica_ConReportingRespondiendo503_DevuelveNoDisponibleSinExcepcion()
    {
        var token = await SeedActorAsync("admin-metrica-503", TodosLosPermisos);
        var widgetId = await AgregarWidgetAsync(token);
        _reportingServer.ForzarRespuesta(HttpStatusCode.ServiceUnavailable);

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/dashboard/widgets/{widgetId}/metrica", token));
        var metrica = await response.Content.ReadFromJsonAsync<DashboardWidgetMetricaResponseDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        metrica!.Estado.Should().Be(1); // NoDisponible
        metrica.ErrorMensaje.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Mismo hallazgo que el test anterior, pero ejerciendo el catch de <c>JsonException</c> en
    /// vez del de código de estado no exitoso.</summary>
    [Fact]
    public async Task ObtenerMetrica_ConRespuestaDeReportingCorrupta_DevuelveNoDisponibleSinExcepcion()
    {
        var token = await SeedActorAsync("admin-metrica-corrupta", TodosLosPermisos);
        var widgetId = await AgregarWidgetAsync(token);
        _reportingServer.ForzarRespuestaCorrupta();

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/dashboard/widgets/{widgetId}/metrica", token));
        var metrica = await response.Content.ReadFromJsonAsync<DashboardWidgetMetricaResponseDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        metrica!.Estado.Should().Be(1); // NoDisponible
    }

    private sealed record DashboardWidgetResponseDto(Guid Id, int Tipo, string Titulo, Guid? WorkflowDefinitionId, int Orden);

    private sealed record DashboardWidgetMetricaResponseDto(
        Guid WidgetId, int Tipo, int Estado, double? PromedioDuracionSegundos, int? CantidadInstanciasFinalizadas, string? ErrorMensaje);
}
