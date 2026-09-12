using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BitCode.Framework.Platform.Workflow;
using BitCode.Framework.Platform.Workflow.Escalamiento;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Workflow.Api.Tests.Integration;

/// <summary>
/// Verifica de punta a punta, contra SQL Server real (Testcontainers), el módulo Workflow (Fase 6,
/// módulo 6): definición -&gt; versión -&gt; publicación -&gt; instancia -&gt; tarea -&gt; aprobación/
/// rechazo -&gt; transición -&gt; historial, más delegación, avance condicional sin tarea humana ("pasos
/// condicionales"), el control de ownership exigido por el gate de salida de Fase 6 ("resolver una tarea
/// ajena sin ser el asignado -&gt; denegado") y la idempotencia del job de escalamiento por SLA (criterio
/// de recuperación: una segunda ejecución del job sobre el mismo estado no duplica el efecto). Genera el
/// JWT directamente vía <see cref="IJwtTokenGenerator"/>, mismo patrón que
/// <c>Sample.Catalogs.Api.Tests/Integration/CatalogsEndpointsIntegrationTests.cs</c>.
/// </summary>
public class WorkflowEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleWorkflowApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleWorkflowApiTestsIdentity", Guid.NewGuid().ToString("N")));

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

    private static readonly Guid TenantId = Guid.NewGuid();

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

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static readonly IReadOnlyList<string> TodosLosPermisos =
    [
        WorkflowPermissions.DefinicionesCrear, WorkflowPermissions.DefinicionesVer,
        WorkflowPermissions.VersionesCrear, WorkflowPermissions.VersionesPublicar,
        WorkflowPermissions.InstanciasIniciar, WorkflowPermissions.InstanciasVer,
        WorkflowPermissions.TareasVer, WorkflowPermissions.TareasResolver, WorkflowPermissions.TareasDelegar,
    ];

    /// <summary>Crea, publica e inicia una instancia de un workflow simple de dos pasos (Pendiente
    /// -&gt; Aprobado/Rechazado), asignando la tarea inicial a <paramref name="asignadoAUserId"/> --
    /// helper compartido por varios tests de esta suite.</summary>
    private async Task<Guid> CrearEIniciarWorkflowSimpleAsync(
        string adminToken, Guid asignadoAUserId, Guid escalarAUserId, int? slaMinutos = null)
    {
        var definitionId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/workflows", adminToken,
                new { codigo = $"APROBACION_{Guid.NewGuid():N}", nombre = "Aprobación simple", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var crearVersionResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/{definitionId}/versiones", adminToken,
                new
                {
                    estados = new object[]
                    {
                        new { codigo = "PENDIENTE", nombre = "Pendiente", esInicial = true, esFinal = false, requiereTarea = true, tituloTarea = "Revisar solicitud", asignadoPorDefectoUserId = asignadoAUserId, slaMinutos, escalarAUserId },
                        new { codigo = "APROBADO", nombre = "Aprobado", esInicial = false, esFinal = true, requiereTarea = false, tituloTarea = (string?)null, asignadoPorDefectoUserId = (Guid?)null, slaMinutos = (int?)null, escalarAUserId = (Guid?)null },
                        new { codigo = "RECHAZADO", nombre = "Rechazado", esInicial = false, esFinal = true, requiereTarea = false, tituloTarea = (string?)null, asignadoPorDefectoUserId = (Guid?)null, slaMinutos = (int?)null, escalarAUserId = (Guid?)null },
                    },
                    transiciones = new object[]
                    {
                        new { desdeCodigo = "PENDIENTE", haciaCodigo = "APROBADO", accion = "Aprobar", reglaExpresion = (string?)null, orden = 1 },
                        new { desdeCodigo = "PENDIENTE", haciaCodigo = "RECHAZADO", accion = "Rechazar", reglaExpresion = (string?)null, orden = 1 },
                    },
                }));
        crearVersionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var versionId = await crearVersionResponse.Content.ReadFromJsonAsync<Guid>();

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/workflows/versiones/{versionId}/publicar", adminToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var iniciarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/workflows/instancias", adminToken,
                new { workflowDefinitionId = definitionId, variables = new Dictionary<string, string>() }));
        iniciarResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var instanceId = await iniciarResponse.Content.ReadFromJsonAsync<Guid>();

        var pendientesResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, "/api/v1/workflows/tareas/pendientes", adminToken));
        pendientesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var instanciaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{instanceId}", adminToken));
        var instanciaDto = await instanciaResponse.Content.ReadFromJsonAsync<WorkflowInstanceResponseDto>();

        var historialResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{instanceId}/historial", adminToken));
        var historial = await historialResponse.Content.ReadFromJsonAsync<List<WorkflowHistorialResponseDto>>();
        historial.Should().Contain(h => h.TipoEvento == "TareaCreada");

        return instanceId;
    }

    private async Task<Guid> ObtenerPrimeraTareaPendienteIdAsync(string token)
    {
        var response = await _client!.SendAsync(BuildRequest(HttpMethod.Get, "/api/v1/workflows/tareas/pendientes", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagina = await response.Content.ReadFromJsonAsync<PagedResultDto<WorkflowTaskResponseDto>>();
        pagina!.Items.Should().NotBeEmpty();
        return pagina.Items[0].Id;
    }

    [Fact]
    public async Task CrearDefinicion_SinAutenticacion_Retorna401()
    {
        var response = await _client!.PostAsJsonAsync(
            "/api/v1/workflows", new { codigo = "X", nombre = "X", descripcion = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FlujoCompleto_Aprobar_TransicionaAEstadoFinalYRegistraHistorial()
    {
        var (_, adminToken) = await SeedActorAsync("admin-aprobar", TodosLosPermisos);
        var asignadoId = Guid.NewGuid();

        // El asignado necesita existir como usuario real (para autenticarse y resolver su propia tarea) --
        // se crea con SeedActorAsync y se usa SU Id real como AsignadoPorDefectoUserId del estado.
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-aprobar", TodosLosPermisos);

        await CrearWorkflowYIniciarAsync(adminToken, asignadoUserId, Guid.NewGuid());

        var tareaId = await ObtenerPrimeraTareaPendienteIdAsync(asignadoToken);

        var resolverResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/tareas/{tareaId}/resolver", asignadoToken,
                new { accion = "Aprobar", comentario = "Todo en orden." }));
        resolverResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tareaResponse = await _client!.SendAsync(BuildRequest(HttpMethod.Get, $"/api/v1/workflows/tareas/{tareaId}", adminToken));
        var tarea = await tareaResponse.Content.ReadFromJsonAsync<WorkflowTaskResponseDto>();
        tarea!.Estado.Should().Be(1); // WorkflowTaskEstado.Resuelta

        var instanciaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{_ultimaInstanceId}", adminToken));
        var instancia = await instanciaResponse.Content.ReadFromJsonAsync<WorkflowInstanceResponseDto>();
        instancia!.Estado.Should().Be(1); // WorkflowInstanceEstado.Finalizada

        var historialResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{_ultimaInstanceId}/historial", adminToken));
        var historial = await historialResponse.Content.ReadFromJsonAsync<List<WorkflowHistorialResponseDto>>();
        historial.Should().Contain(h => h.TipoEvento == "InstanciaIniciada");
        historial.Should().Contain(h => h.TipoEvento == "TareaCreada");
        historial.Should().Contain(h => h.TipoEvento == "TareaAprobar");
        historial.Should().Contain(h => h.TipoEvento == "InstanciaFinalizada");
    }

    [Fact]
    public async Task FlujoCompleto_Rechazar_TransicionaAEstadoFinalDeRechazo()
    {
        var (_, adminToken) = await SeedActorAsync("admin-rechazar", TodosLosPermisos);
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-rechazar", TodosLosPermisos);

        await CrearWorkflowYIniciarAsync(adminToken, asignadoUserId, Guid.NewGuid());
        var tareaId = await ObtenerPrimeraTareaPendienteIdAsync(asignadoToken);

        var resolverResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/tareas/{tareaId}/resolver", asignadoToken,
                new { accion = "Rechazar", comentario = "No cumple los requisitos." }));
        resolverResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var instanciaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{_ultimaInstanceId}", adminToken));
        var instancia = await instanciaResponse.Content.ReadFromJsonAsync<WorkflowInstanceResponseDto>();
        instancia!.Estado.Should().Be(1); // Finalizada

        var historialResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{_ultimaInstanceId}/historial", adminToken));
        var historial = await historialResponse.Content.ReadFromJsonAsync<List<WorkflowHistorialResponseDto>>();
        historial.Should().Contain(h => h.TipoEvento == "TareaRechazar");
    }

    /// <summary>Criterio de aceptación explícito del gate de salida de Fase 6: "Workflow tiene pruebas de
    /// seguridad" -- un actor con el permiso RBAC <see cref="WorkflowPermissions.TareasResolver"/> pero
    /// que NO es el asignado actual de la tarea no puede resolverla.</summary>
    [Fact]
    public async Task ResolverTarea_SinSerElAsignado_Retorna403()
    {
        var (_, adminToken) = await SeedActorAsync("admin-ownership", TodosLosPermisos);
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-ownership", TodosLosPermisos);
        var (_, actorAjenoToken) = await SeedActorAsync("ajeno-ownership", [WorkflowPermissions.TareasResolver, WorkflowPermissions.TareasVer]);

        await CrearWorkflowYIniciarAsync(adminToken, asignadoUserId, Guid.NewGuid());
        var tareaId = await ObtenerPrimeraTareaPendienteIdAsync(asignadoToken);

        var resolverResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/tareas/{tareaId}/resolver", actorAjenoToken,
                new { accion = "Aprobar", comentario = (string?)null }));

        resolverResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DelegarTarea_ElNuevoAsignadoPuedeResolverYElOriginalYaNo()
    {
        var (_, adminToken) = await SeedActorAsync("admin-delegar", TodosLosPermisos);
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-delegar", TodosLosPermisos);
        var (delegadoUserId, delegadoToken) = await SeedActorAsync("delegado-delegar", TodosLosPermisos);

        await CrearWorkflowYIniciarAsync(adminToken, asignadoUserId, Guid.NewGuid());
        var tareaId = await ObtenerPrimeraTareaPendienteIdAsync(asignadoToken);

        var delegarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/tareas/{tareaId}/delegar", asignadoToken,
                new { nuevoAsignadoUserId = delegadoUserId }));
        delegarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // El asignado ORIGINAL ya no puede resolverla -- la delegación cambió el ownership.
        var resolverPorOriginalResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/tareas/{tareaId}/resolver", asignadoToken,
                new { accion = "Aprobar", comentario = (string?)null }));
        resolverPorOriginalResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var resolverPorDelegadoResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/tareas/{tareaId}/resolver", delegadoToken,
                new { accion = "Aprobar", comentario = (string?)null }));
        resolverPorDelegadoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>"Pasos condicionales" del Plan Maestro: un estado sin tarea humana avanza automáticamente
    /// según la variable de contexto, sin ninguna intervención humana -- un monto bajo finaliza de
    /// inmediato; un monto alto cae en revisión manual.</summary>
    [Fact]
    public async Task Instancia_ConEstadoCondicionalSinTarea_AvanzaAutomaticamenteSegunVariables()
    {
        var (_, adminToken) = await SeedActorAsync("admin-condicional", TodosLosPermisos);
        var (revisorUserId, revisorToken) = await SeedActorAsync("revisor-condicional", TodosLosPermisos);

        var definitionId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/workflows", adminToken,
                new { codigo = $"GASTOS_{Guid.NewGuid():N}", nombre = "Gastos", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var crearVersionResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/{definitionId}/versiones", adminToken,
                new
                {
                    estados = new object[]
                    {
                        new { codigo = "INICIO", nombre = "Inicio", esInicial = true, esFinal = false, requiereTarea = false, tituloTarea = (string?)null, asignadoPorDefectoUserId = (Guid?)null, slaMinutos = (int?)null, escalarAUserId = (Guid?)null },
                        new { codigo = "APROBADO_AUTO", nombre = "Aprobado automático", esInicial = false, esFinal = true, requiereTarea = false, tituloTarea = (string?)null, asignadoPorDefectoUserId = (Guid?)null, slaMinutos = (int?)null, escalarAUserId = (Guid?)null },
                        new { codigo = "REVISION_MANUAL", nombre = "Revisión manual", esInicial = false, esFinal = false, requiereTarea = true, tituloTarea = "Revisar gasto alto", asignadoPorDefectoUserId = revisorUserId, slaMinutos = (int?)null, escalarAUserId = (Guid?)null },
                    },
                    transiciones = new object[]
                    {
                        new { desdeCodigo = "INICIO", haciaCodigo = "APROBADO_AUTO", accion = "Avanzar", reglaExpresion = "monto<=1000", orden = 1 },
                        new { desdeCodigo = "INICIO", haciaCodigo = "REVISION_MANUAL", accion = "Avanzar", reglaExpresion = (string?)null, orden = 2 },
                        new { desdeCodigo = "REVISION_MANUAL", haciaCodigo = "APROBADO_AUTO", accion = "Avanzar", reglaExpresion = (string?)null, orden = 1 },
                    },
                }));
        crearVersionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var versionId = await crearVersionResponse.Content.ReadFromJsonAsync<Guid>();

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/workflows/versiones/{versionId}/publicar", adminToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var instanciaBajaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/workflows/instancias", adminToken,
                new { workflowDefinitionId = definitionId, variables = new Dictionary<string, string> { ["monto"] = "500" } }));
        instanciaBajaResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var instanciaBajaId = await instanciaBajaResponse.Content.ReadFromJsonAsync<Guid>();

        var instanciaBaja = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{instanciaBajaId}", adminToken)))
            .Content.ReadFromJsonAsync<WorkflowInstanceResponseDto>();
        instanciaBaja!.Estado.Should().Be(1); // Finalizada de inmediato, sin tarea humana.

        var instanciaAltaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/workflows/instancias", adminToken,
                new { workflowDefinitionId = definitionId, variables = new Dictionary<string, string> { ["monto"] = "50000" } }));
        instanciaAltaResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var instanciaAltaId = await instanciaAltaResponse.Content.ReadFromJsonAsync<Guid>();

        var instanciaAlta = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{instanciaAltaId}", adminToken)))
            .Content.ReadFromJsonAsync<WorkflowInstanceResponseDto>();
        instanciaAlta!.Estado.Should().Be(0); // EnCurso -- se detuvo en la revisión manual.

        var tareaId = await ObtenerPrimeraTareaPendienteIdAsync(revisorToken);
        var resolverResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/workflows/tareas/{tareaId}/resolver", revisorToken,
                new { accion = "Avanzar", comentario = (string?)null }));
        resolverResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Criterio de recuperación del gate de salida de Fase 6: el job de escalamiento por SLA es
    /// idempotente ante una segunda ejecución sobre el mismo estado de base de datos (equivalente, en
    /// espíritu, a lo que pasaría si el proceso que lo ejecuta se cae a mitad de un ciclo y Quartz
    /// reintenta el disparo en otro nodo, ver <c>docs/guia-quartz-ha.md</c> sección 5) -- una segunda
    /// corrida NO duplica la reasignación ni agrega una segunda fila de historial.
    /// </summary>
    [Fact]
    public async Task WorkflowEscalamientoJob_EjecutadoDosVeces_NoDuplicaLaEscalacion()
    {
        var (_, adminToken) = await SeedActorAsync("admin-escalamiento", TodosLosPermisos);
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-escalamiento", TodosLosPermisos);
        var escalarAUserId = Guid.NewGuid();

        // SLA de -1 minuto: la tarea nace ya vencida, sin tener que esperar un timer real en el test.
        await CrearWorkflowYIniciarAsync(adminToken, asignadoUserId, escalarAUserId, slaMinutos: -1);
        var tareaId = await ObtenerPrimeraTareaPendienteIdAsync(asignadoToken);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var job = new WorkflowEscalamientoJob(scope.ServiceProvider.GetRequiredService<WorkflowDbContext>());
            await job.EscalarVencidasAsync(CancellationToken.None);
        }

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var job = new WorkflowEscalamientoJob(scope.ServiceProvider.GetRequiredService<WorkflowDbContext>());
            await job.EscalarVencidasAsync(CancellationToken.None);
        }

        var tareaResponse = await _client!.SendAsync(BuildRequest(HttpMethod.Get, $"/api/v1/workflows/tareas/{tareaId}", adminToken));
        var tarea = await tareaResponse.Content.ReadFromJsonAsync<WorkflowTaskResponseDto>();
        tarea!.Escalada.Should().BeTrue();
        tarea.AsignadoAUserId.Should().Be(escalarAUserId);

        var historialResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/workflows/instancias/{_ultimaInstanceId}/historial", adminToken));
        var historial = await historialResponse.Content.ReadFromJsonAsync<List<WorkflowHistorialResponseDto>>();
        historial!.Count(h => h.TipoEvento == "TareaEscalada").Should().Be(1);
    }

    private Guid _ultimaInstanceId;

    private async Task CrearWorkflowYIniciarAsync(string adminToken, Guid asignadoAUserId, Guid escalarAUserId, int? slaMinutos = null)
    {
        _ultimaInstanceId = await CrearEIniciarWorkflowSimpleAsync(adminToken, asignadoAUserId, escalarAUserId, slaMinutos);
    }

    private sealed record WorkflowInstanceResponseDto(Guid Id, Guid WorkflowDefinitionId, Guid WorkflowVersionId, Guid EstadoActualId, int Estado, Dictionary<string, string> Variables, DateTime? FinalizadaAtUtc);

    private sealed record WorkflowTaskResponseDto(Guid Id, Guid WorkflowInstanceId, Guid WorkflowStateId, string Titulo, Guid AsignadoAUserId, int Estado, string? AccionResuelta, DateTime? SlaVencimientoUtc, bool Escalada);

    private sealed record WorkflowHistorialResponseDto(Guid Id, DateTime FechaUtc, string TipoEvento, string Detalle, Guid? ActorUserId);

    private sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
}
