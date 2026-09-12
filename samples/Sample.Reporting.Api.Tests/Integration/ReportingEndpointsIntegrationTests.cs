using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BitCode.Framework.Platform.Reporting;
using BitCode.Framework.Platform.Reporting.WorkflowInstancias;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Application.Inbox;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Reporting.Api.Tests.Integration;

/// <summary>
/// Verifica, contra SQL Server real (Testcontainers), el módulo Reporting (Fase 6, módulo 11): el
/// read-model de referencia (<see cref="ReporteWorkflowInstancia"/>) se construye/actualiza
/// EXCLUSIVAMENTE consumiendo los dos eventos de integración públicos de Workflow (Fase 6, módulo 6) --
/// nunca hay una llamada a <c>WorkflowDbContext</c> ni a ningún comando de Workflow desde este proceso.
/// </summary>
/// <remarks>
/// Este host NO tiene Workflow corriendo en el mismo proceso (mismo criterio que
/// <c>Sample.TaskInbox.Api.Tests</c>, ver <c>InfrastructureModule</c> y "Límites conocidos" de
/// <c>docs/guia-reporting.md</c>). Por eso esta suite SIMULA la llegada de un evento de Workflow
/// construyendo directamente sus <c>record</c> públicos (<see cref="WorkflowInstanciaIniciadaIntegrationEvent"/>/
/// <see cref="WorkflowInstanciaFinalizadaIntegrationEvent"/>) e invocando el mismo mecanismo que usaría un
/// <c>KafkaEventConsumer&lt;TEvent&gt;</c> real (<c>IInboxMessageProcessor.ProcessAsync</c>, F1-24/F3-04)
/// contra el <see cref="IEventConsumer{TEvent}"/> que <c>AddSharedReporting()</c> ya registra -- exactamente
/// el mismo código que correría en producción, solo sin la infraestructura de broker real.
///
/// Como los dos eventos de Workflow no llevan <c>TenantId</c> (misma limitación real ya documentada por
/// <c>docs/guia-taskinbox.md</c>), <see cref="SimularEventoAsync{TEvent}"/> manufactura un
/// <see cref="HttpContext"/> con el claim de tenant esperado antes de invocar el consumidor.
/// </remarks>
public class ReportingEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private static readonly Guid TenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleReportingApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleReportingApiTestsIdentity", Guid.NewGuid().ToString("N")));

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

        await _sqlServerFixture.DisposeAsync();
    }

    private async Task<string> SeedActorAsync(string userName, IReadOnlyList<string>? permissions = null)
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

        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.local", TenantId = TenantId };
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

    private static readonly IReadOnlyList<string> TodosLosPermisos =
        [ReportingPermissions.WorkflowInstanciasVer, ReportingPermissions.WorkflowInstanciasExportar];

    /// <summary>Simula la entrega de un evento de integración de Workflow -- ver remarks de la clase.</summary>
    private async Task SimularEventoAsync<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent
    {
        await using var scope = _factory!.Services.CreateAsyncScope();

        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity("SimuladorDeEventosDeWorkflow");
        identity.AddClaim(new Claim(TenantClaimTypes.TenantId, TenantId.ToString()));
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var processor = scope.ServiceProvider.GetRequiredService<IInboxMessageProcessor>();
        var consumer = scope.ServiceProvider.GetRequiredService<IEventConsumer<TEvent>>();

        await processor.ProcessAsync(
            integrationEvent.EventId.ToString(),
            integrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent),
            ct => consumer.ConsumeAsync(integrationEvent, ct));
    }

    private async Task<ReporteWorkflowInstanciaResponseDto?> ObtenerAsync(Guid id, string token)
    {
        var response = await _client!.SendAsync(BuildRequest(HttpMethod.Get, $"/api/v1/reporting/workflow-instancias/{id}", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<ReporteWorkflowInstanciaResponseDto>();
    }

    [Fact]
    public async Task ListarReporte_SinAutenticacion_Retorna401()
    {
        var response = await _client!.GetAsync("/api/v1/reporting/workflow-instancias");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListarReporte_SinPermiso_Retorna403()
    {
        var token = await SeedActorAsync("sin-permiso-ver");

        var response = await _client!.SendAsync(BuildRequest(HttpMethod.Get, "/api/v1/reporting/workflow-instancias", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportarReporte_ConPermisoDeVerPeroSinPermisoDeExportar_Retorna403()
    {
        var token = await SeedActorAsync("solo-ver", [ReportingPermissions.WorkflowInstanciasVer]);

        var response = await _client!.SendAsync(BuildRequest(HttpMethod.Get, "/api/v1/reporting/workflow-instancias/exportar", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WorkflowInstanciaIniciada_CreaLaFilaEnCurso()
    {
        var token = await SeedActorAsync("actor-iniciada", TodosLosPermisos);
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid(), Guid.NewGuid()));

        var reporte = await ObtenerAsync(instanceId, token);
        reporte.Should().NotBeNull();
        reporte!.WorkflowDefinitionId.Should().Be(definitionId);
        reporte.Estado.Should().Be((int)ReporteWorkflowInstanciaEstado.EnCurso);
        reporte.IniciadaAtUtc.Should().NotBeNull();
        reporte.FinalizadaAtUtc.Should().BeNull();
        reporte.DuracionSegundos.Should().BeNull();
    }

    /// <summary>Criterio de aceptación de F1-24/F3-04 ("duplicados no repiten efectos") aplicado al
    /// consumidor de este módulo: reentregar el MISMO evento (mismo <c>EventId</c>) no duplica la fila.</summary>
    [Fact]
    public async Task WorkflowInstanciaIniciada_ReentregadaConElMismoEventId_NoDuplicaLaFila()
    {
        var token = await SeedActorAsync("actor-duplicado", TodosLosPermisos);
        var evento = new WorkflowInstanciaIniciadaIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await SimularEventoAsync(evento);
        await SimularEventoAsync(evento);

        var response = await _client!.SendAsync(BuildRequest(
            HttpMethod.Get,
            $"/api/v1/reporting/workflow-instancias?workflowDefinitionId={evento.WorkflowDefinitionId}",
            token));
        var pagina = await response.Content.ReadFromJsonAsync<PagedResultDto<ReporteWorkflowInstanciaResponseDto>>();
        pagina!.Items.Count(i => i.Id == evento.WorkflowInstanceId).Should().Be(1);
    }

    [Fact]
    public async Task WorkflowInstanciaFinalizada_DespuesDeIniciada_CalculaDuracionYCambiaEstado()
    {
        var token = await SeedActorAsync("actor-finalizada", TodosLosPermisos);
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid(), Guid.NewGuid()));
        await Task.Delay(1100); // Asegura una duración > 0 segundos medible con precisión de entero.
        await SimularEventoAsync(new WorkflowInstanciaFinalizadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid()));

        var reporte = await ObtenerAsync(instanceId, token);
        reporte!.Estado.Should().Be((int)ReporteWorkflowInstanciaEstado.Finalizada);
        reporte.FinalizadaAtUtc.Should().NotBeNull();
        reporte.DuracionSegundos.Should().NotBeNull();
        reporte.DuracionSegundos!.Value.Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>Caso de borde documentado en <see cref="ReporteWorkflowInstancia"/>: los dos eventos de
    /// Workflow viven en tópicos DISTINTOS, así que nada garantiza que la finalización llegue después del
    /// inicio de la misma instancia -- este módulo no pierde el evento de finalización en ese escenario, y
    /// como AMBOS eventos llevan <c>WorkflowDefinitionId</c>, la fila nace con toda la información
    /// necesaria para el reporte agregado salvo la fecha/duración de inicio.</summary>
    [Fact]
    public async Task WorkflowInstanciaFinalizada_SinInicioPrevio_CreaLaFilaYaFinalizadaSinDuracionTodavia()
    {
        var token = await SeedActorAsync("actor-fuera-de-orden", TodosLosPermisos);
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var estadoFinalId = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaFinalizadaIntegrationEvent(instanceId, definitionId, estadoFinalId));

        var reporte = await ObtenerAsync(instanceId, token);
        reporte!.WorkflowDefinitionId.Should().Be(definitionId);
        reporte.Estado.Should().Be((int)ReporteWorkflowInstanciaEstado.Finalizada);
        reporte.EstadoFinalId.Should().Be(estadoFinalId);
        reporte.IniciadaAtUtc.Should().BeNull();
        reporte.DuracionSegundos.Should().BeNull();
    }

    /// <summary>Continuación del caso anterior: el inicio tardío completa <c>IniciadaAtUtc</c>/
    /// <c>DuracionSegundos</c> sin revertir el estado ya finalizado.</summary>
    [Fact]
    public async Task WorkflowInstanciaIniciada_LlegaDespuesDeLaFinalizacion_CompletaLaDuracionSinRevertirElEstado()
    {
        var token = await SeedActorAsync("actor-inicio-tardio", TodosLosPermisos);
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaFinalizadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid()));
        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid(), Guid.NewGuid()));

        var reporte = await ObtenerAsync(instanceId, token);
        reporte!.Estado.Should().Be((int)ReporteWorkflowInstanciaEstado.Finalizada);
        reporte.IniciadaAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ListarReporte_FiltraPorEstado()
    {
        var token = await SeedActorAsync("actor-filtro", TodosLosPermisos);
        var definitionId = Guid.NewGuid();
        var instanciaEnCurso = Guid.NewGuid();
        var instanciaFinalizada = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanciaEnCurso, definitionId, Guid.NewGuid(), Guid.NewGuid()));
        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanciaFinalizada, definitionId, Guid.NewGuid(), Guid.NewGuid()));
        await SimularEventoAsync(new WorkflowInstanciaFinalizadaIntegrationEvent(instanciaFinalizada, definitionId, Guid.NewGuid()));

        var response = await _client!.SendAsync(BuildRequest(
            HttpMethod.Get,
            $"/api/v1/reporting/workflow-instancias?workflowDefinitionId={definitionId}&estado={(int)ReporteWorkflowInstanciaEstado.EnCurso}",
            token));
        var pagina = await response.Content.ReadFromJsonAsync<PagedResultDto<ReporteWorkflowInstanciaResponseDto>>();
        pagina!.Items.Should().ContainSingle(i => i.Id == instanciaEnCurso);
        pagina.Items.Should().NotContain(i => i.Id == instanciaFinalizada);
    }

    [Fact]
    public async Task PromedioDuracion_AgregaPorDefinicion()
    {
        var token = await SeedActorAsync("actor-promedio", TodosLosPermisos);
        var definitionId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid(), Guid.NewGuid()));
        await Task.Delay(1100);
        await SimularEventoAsync(new WorkflowInstanciaFinalizadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid()));

        var response = await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/reporting/workflow-instancias/promedio-duracion?workflowDefinitionId={definitionId}", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var promedios = await response.Content.ReadFromJsonAsync<List<PromedioDuracionPorDefinicionResponseDto>>();
        var fila = promedios.Should().ContainSingle(p => p.WorkflowDefinitionId == definitionId).Subject;
        fila.CantidadInstanciasFinalizadas.Should().Be(1);
        fila.PromedioDuracionSegundos.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task PromedioDuracion_NoIncluyeInstanciasEnCurso()
    {
        var token = await SeedActorAsync("actor-promedio-sin-finalizar", TodosLosPermisos);
        var definitionId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid(), Guid.NewGuid()));

        var response = await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/reporting/workflow-instancias/promedio-duracion?workflowDefinitionId={definitionId}", token));
        var promedios = await response.Content.ReadFromJsonAsync<List<PromedioDuracionPorDefinicionResponseDto>>();

        promedios.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportarReporte_DevuelveCsvConEncabezadoYFilas()
    {
        var token = await SeedActorAsync("actor-exportar", TodosLosPermisos);
        var definitionId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await SimularEventoAsync(new WorkflowInstanciaIniciadaIntegrationEvent(instanceId, definitionId, Guid.NewGuid(), Guid.NewGuid()));

        var response = await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/reporting/workflow-instancias/exportar?workflowDefinitionId={definitionId}", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        var lineas = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lineas[0].Should().Be("WorkflowInstanceId,WorkflowDefinitionId,Estado,IniciadaAtUtc,FinalizadaAtUtc,DuracionSegundos,EstadoFinalId");
        lineas.Should().Contain(l => l.StartsWith(instanceId.ToString()));
    }

    private sealed record ReporteWorkflowInstanciaResponseDto(
        Guid Id, Guid WorkflowDefinitionId, int Estado, DateTime? IniciadaAtUtc, DateTime? FinalizadaAtUtc,
        int? DuracionSegundos, Guid? EstadoFinalId);

    private sealed record PromedioDuracionPorDefinicionResponseDto(
        Guid WorkflowDefinitionId, int CantidadInstanciasFinalizadas, double PromedioDuracionSegundos);

    private sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
}
