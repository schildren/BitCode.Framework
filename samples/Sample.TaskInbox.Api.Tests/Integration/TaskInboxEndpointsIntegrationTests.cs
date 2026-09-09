using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BitCode.Framework.Platform.TaskInbox;
using BitCode.Framework.Platform.TaskInbox.Bandeja;
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

namespace Sample.TaskInbox.Api.Tests.Integration;

/// <summary>
/// Verifica, contra SQL Server real (Testcontainers), el módulo Task Inbox (Fase 6, módulo 7): el
/// read-model propio (<see cref="TaskInboxItem"/>) se construye/actualiza EXCLUSIVAMENTE consumiendo
/// los tres eventos de integración públicos de Workflow (Fase 6, módulo 6) -- nunca hay una llamada a
/// <c>WorkflowDbContext</c> ni a ningún comando de Workflow desde este proceso.
/// </summary>
/// <remarks>
/// Este host NO tiene Workflow corriendo en el mismo proceso (ver <c>InfrastructureModule</c>, sección
/// "Límites conocidos" de <c>docs/guia-taskinbox.md</c>: combinar dos <c>AddSharedPersistence&lt;T&gt;</c>
/// de dos bounded contexts de Fase 6 en un mismo host es hoy inseguro). Por eso esta suite SIMULA la
/// llegada de un evento de Workflow construyendo directamente sus <c>record</c> públicos
/// (<see cref="TareaAsignadaIntegrationEvent"/>/<see cref="TareaAprobadaIntegrationEvent"/>/
/// <see cref="TareaRechazadaIntegrationEvent"/>) e invocando el mismo mecanismo que usaría un
/// <c>KafkaEventConsumer&lt;TEvent&gt;</c> real (<c>IInboxMessageProcessor.ProcessAsync</c>, F1-24/F3-04,
/// ver <c>docs/guia-inbox-consumer.md</c>) contra el <see cref="IEventConsumer{TEvent}"/> que
/// <c>AddSharedTaskInbox()</c> ya registra -- exactamente el mismo código que correría en producción, solo
/// sin la infraestructura de broker real (mismo estado que el resto de los módulos de Fase 6, ver
/// <c>docs/catalogo-eventos.md</c>).
///
/// Como los tres eventos de Workflow no llevan <c>TenantId</c> (limitación real, documentada en
/// <c>docs/guia-taskinbox.md</c> y ya señalada por <c>docs/guia-workflow.md</c> para
/// <c>WorkflowEscalamientoJob</c>), <see cref="SimularEventoAsync{TEvent}"/> manufactura un
/// <see cref="HttpContext"/> con el claim de tenant esperado ANTES de invocar el consumidor, para que
/// <c>TenantSaveChangesInterceptor</c> (F1-12) estampe el <c>TenantId</c> correcto -- una simulación
/// explícita de la información de tenant que, en un despliegue real sin resolver ese hueco de
/// infraestructura compartida, este proceso no tendría de dónde obtener.
/// </remarks>
public class TaskInboxEndpointsIntegrationTests : IAsyncLifetime
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
            _sqlServerFixture.BuildIsolatedConnectionString("SampleTaskInboxApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleTaskInboxApiTestsIdentity", Guid.NewGuid().ToString("N")));

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

    private async Task<(Guid UserId, string Token)> SeedActorAsync(string userName, IReadOnlyList<string>? permissions = null)
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

        var token = tokenGenerator.GenerateAccessToken(user, [roleName], []);
        return (user.Id, token);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        return request;
    }

    private static readonly IReadOnlyList<string> TodosLosPermisos =
        [TaskInboxPermissions.BandejaVer, TaskInboxPermissions.BandejaMarcarLeida];

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

    private async Task<PagedResultDto<TaskInboxItemResponseDto>> ObtenerBandejaAsync(string token, string query = "")
    {
        var response = await _client!.SendAsync(BuildRequest(HttpMethod.Get, $"/api/v1/taskinbox/bandeja{query}", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PagedResultDto<TaskInboxItemResponseDto>>())!;
    }

    [Fact]
    public async Task ObtenerBandeja_SinAutenticacion_Retorna401()
    {
        var response = await _client!.GetAsync("/api/v1/taskinbox/bandeja");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TareaAsignada_AparecePendienteEnLaBandejaDelAsignado()
    {
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-alta", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, workflowInstanceId, asignadoUserId));

        var bandeja = await ObtenerBandejaAsync(asignadoToken);
        bandeja.Items.Should().ContainSingle(i => i.Id == workflowTaskId && i.Estado == (int)TaskInboxEstado.Pendiente);
    }

    /// <summary>Criterio de aceptación de F1-24/F3-04 ("duplicados no repiten efectos") aplicado al
    /// consumidor de este módulo: reentregar el MISMO evento (mismo <c>EventId</c>) no duplica la fila
    /// ni reinicia su estado.</summary>
    [Fact]
    public async Task TareaAsignada_ReentregadaConElMismoEventId_NoDuplicaLaFila()
    {
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-duplicado", TodosLosPermisos);
        var evento = new TareaAsignadaIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), asignadoUserId);

        await SimularEventoAsync(evento);
        await SimularEventoAsync(evento);

        var bandeja = await ObtenerBandejaAsync(asignadoToken);
        bandeja.Items.Count(i => i.Id == evento.WorkflowTaskId).Should().Be(1);
    }

    [Fact]
    public async Task TareaAprobada_ActualizaElEstadoDeLaFilaExistente()
    {
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-aprobada", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, workflowInstanceId, asignadoUserId));
        await SimularEventoAsync(new TareaAprobadaIntegrationEvent(workflowTaskId, workflowInstanceId, asignadoUserId));

        var bandeja = await ObtenerBandejaAsync(asignadoToken);
        var item = bandeja.Items.Should().ContainSingle(i => i.Id == workflowTaskId).Subject;
        item.Estado.Should().Be((int)TaskInboxEstado.Aprobada);
        item.ResueltaPorUserId.Should().Be(asignadoUserId);
    }

    /// <summary>Caso de borde documentado en <see cref="TaskInboxItem.CrearYaResuelta"/>: los tres
    /// eventos de Workflow viven en tópicos DISTINTOS, así que nada garantiza que
    /// <c>Workflow.TareaAprobada</c> llegue después de <c>Workflow.TareaAsignada</c> de la misma
    /// tarea -- este módulo no pierde el evento de resolución en ese escenario, pero tampoco le
    /// atribuye la tarea a quien la resolvió (podría ser un actor distinto del asignado original, ver
    /// hallazgo de auditoría de arquitectura 2026-09-09): el ítem nace con <c>AsignadoAUserId</c>
    /// desconocido (no visible en la bandeja de nadie) hasta que la asignación tardía llega.</summary>
    [Fact]
    public async Task TareaAprobada_SinAsignacionPrevia_CreaLaFilaYaResueltaSinDuenioTodavia()
    {
        var (resolutorUserId, resolutorToken) = await SeedActorAsync("resolutor-fuera-de-orden", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAprobadaIntegrationEvent(workflowTaskId, workflowInstanceId, resolutorUserId));

        // Quien resolvió la tarea NO es necesariamente el asignado original -- el ítem todavía no
        // aparece en la bandeja de nadie porque AsignadoAUserId sigue siendo el marcador "desconocido"
        // (Guid.Empty) hasta que la asignación tardía llegue.
        var bandejaDelResolutor = await ObtenerBandejaAsync(resolutorToken);
        bandejaDelResolutor.Items.Should().NotContain(i => i.Id == workflowTaskId);
    }

    /// <summary>Corrige el hallazgo Crítico de la auditoría de arquitectura (2026-09-09): una asignación
    /// tardía que llega DESPUÉS de una resolución ya aplicada (ver test anterior) debe corregir el
    /// dueño de la fila sin revertir el estado a Pendiente ni borrar quién la resolvió.</summary>
    [Fact]
    public async Task TareaAsignada_LlegaDespuesDeLaResolucion_CorrigeElDuenioSinRevertirLaResolucion()
    {
        var (resolutorUserId, resolutorToken) = await SeedActorAsync("resolutor-tardio", TodosLosPermisos);
        var (asignadoOriginalUserId, asignadoOriginalToken) = await SeedActorAsync("asignado-original-tardio", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();

        await SimularEventoAsync(new TareaRechazadaIntegrationEvent(workflowTaskId, workflowInstanceId, resolutorUserId));
        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, workflowInstanceId, asignadoOriginalUserId));

        var bandejaDelAsignado = await ObtenerBandejaAsync(asignadoOriginalToken);
        var item = bandejaDelAsignado.Items.Should().ContainSingle(i => i.Id == workflowTaskId).Subject;
        item.Estado.Should().Be((int)TaskInboxEstado.Rechazada);
        item.ResueltaPorUserId.Should().Be(resolutorUserId);

        var bandejaDelResolutor = await ObtenerBandejaAsync(resolutorToken);
        bandejaDelResolutor.Items.Should().NotContain(i => i.Id == workflowTaskId);
    }

    [Fact]
    public async Task ListarBandeja_FiltraPorEstado()
    {
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-filtro", TodosLosPermisos);
        var tareaPendienteId = Guid.NewGuid();
        var tareaAprobadaId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(tareaPendienteId, instanceId, asignadoUserId));
        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(tareaAprobadaId, instanceId, asignadoUserId));
        await SimularEventoAsync(new TareaAprobadaIntegrationEvent(tareaAprobadaId, instanceId, asignadoUserId));

        var pendientes = await ObtenerBandejaAsync(asignadoToken, "?estado=0");
        pendientes.Items.Should().ContainSingle(i => i.Id == tareaPendienteId);
        pendientes.Items.Should().NotContain(i => i.Id == tareaAprobadaId);

        var aprobadas = await ObtenerBandejaAsync(asignadoToken, "?estado=1");
        aprobadas.Items.Should().ContainSingle(i => i.Id == tareaAprobadaId);
    }

    [Fact]
    public async Task MarcarComoLeida_SinSerElAsignado_Retorna403()
    {
        var (asignadoUserId, _) = await SeedActorAsync("asignado-leida-ownership", TodosLosPermisos);
        var (_, actorAjenoToken) = await SeedActorAsync("ajeno-leida-ownership", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, Guid.NewGuid(), asignadoUserId));

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/taskinbox/bandeja/{workflowTaskId}/marcar-leida", actorAjenoToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MarcarComoLeida_PorElAsignado_QuedaMarcadaEnLaBandeja()
    {
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-leida", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, Guid.NewGuid(), asignadoUserId));

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/taskinbox/bandeja/{workflowTaskId}/marcar-leida", asignadoToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var itemResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/taskinbox/bandeja/{workflowTaskId}", asignadoToken));
        var item = await itemResponse.Content.ReadFromJsonAsync<TaskInboxItemResponseDto>();
        item!.LeidoAtUtc.Should().NotBeNull();
    }

    /// <summary>Corrige el hallazgo Alto (IDOR) de la auditoría de arquitectura (2026-09-09):
    /// <c>GET /api/v1/taskinbox/bandeja/{id}</c> no verificaba ownership -- cualquier actor con el
    /// permiso RBAC genérico podía leer el ítem de bandeja de OTRO usuario por id.</summary>
    [Fact]
    public async Task ObtenerItemBandeja_SinSerElAsignado_Retorna403()
    {
        var (asignadoUserId, _) = await SeedActorAsync("asignado-idor", TodosLosPermisos);
        var (_, actorAjenoToken) = await SeedActorAsync("actor-ajeno-idor", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, Guid.NewGuid(), asignadoUserId));

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/taskinbox/bandeja/{workflowTaskId}", actorAjenoToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Refleja una delegación/escalamiento real de Workflow: la bandeja del asignado ORIGINAL
    /// deja de mostrar la tarea entre sus pendientes propios y pasa a la del nuevo asignado -- este
    /// módulo nunca decide la reasignación, solo la refleja.</summary>
    [Fact]
    public async Task TareaReasignada_CambiaDeAsignadoEnLaBandeja()
    {
        var (asignadoOriginalUserId, asignadoOriginalToken) = await SeedActorAsync("asignado-original-reasig", TodosLosPermisos);
        var (nuevoAsignadoUserId, nuevoAsignadoToken) = await SeedActorAsync("nuevo-asignado-reasig", TodosLosPermisos);
        var workflowTaskId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, instanceId, asignadoOriginalUserId));
        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, instanceId, nuevoAsignadoUserId));

        var bandejaOriginal = await ObtenerBandejaAsync(asignadoOriginalToken);
        bandejaOriginal.Items.Should().NotContain(i => i.Id == workflowTaskId);

        var bandejaNueva = await ObtenerBandejaAsync(nuevoAsignadoToken);
        bandejaNueva.Items.Should().ContainSingle(i => i.Id == workflowTaskId);
    }

    private sealed record TaskInboxItemResponseDto(
        Guid Id, Guid WorkflowInstanceId, Guid AsignadoAUserId, int Estado, DateTime AsignadaAtUtc,
        Guid? ResueltaPorUserId, DateTime? ResueltaAtUtc, DateTime? LeidoAtUtc);

    private sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
}
