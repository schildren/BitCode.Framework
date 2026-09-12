using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using BitCode.Framework.Platform.IntegrationHub;
using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Platform.IntegrationHub.Envio;
using BitCode.Framework.Platform.IntegrationHub.Procesamiento;
using BitCode.Framework.Platform.IntegrationHub.Solicitudes;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sample.IntegrationHub.Api.Tests.Integration;

/// <summary>
/// Verifica, contra SQL Server real (Testcontainers) y un servidor HTTP real embebido en el mismo
/// proceso (<see cref="TestExternalHttpServer"/>, Kestrel real -- NO un mock de <c>HttpClient</c>), el
/// módulo Integration Hub (Fase 6, módulo 9): conectores, mapping campo-a-campo, credenciales (API key
/// resuelta vía <see cref="ISecretProvider"/>), la cola de <see cref="IntegrationRequest"/> procesada por
/// <see cref="IntegrationOutboundProcessorJob"/> con reintentos (F3-07 reutilizado) y el tracking de cada
/// intento (<see cref="IntegrationRequestLog"/>).
/// </summary>
public class IntegrationHubEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private readonly TestExternalHttpServer _externalServer = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly IReadOnlyList<string> TodosLosPermisos =
    [
        IntegrationHubPermissions.ConectoresAdministrar,
        IntegrationHubPermissions.ConectoresVer,
        IntegrationHubPermissions.SolicitudesEnviar,
        IntegrationHubPermissions.SolicitudesVer,
    ];

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sqlServerFixture.InitializeAsync(), _externalServer.StartAsync());

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleIntegrationHubApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleIntegrationHubApiTestsIdentity", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("IntegrationHub__Retry__MaxAttempts", "2");
        Environment.SetEnvironmentVariable("IntegrationHub__Retry__BaseDelay", "00:00:00");
        Environment.SetEnvironmentVariable("IntegrationHub__Retry__MaxDelay", "00:00:00");
        Environment.SetEnvironmentVariable("Secrets__Values__conector-api-key-test", "el-secreto-real-del-conector");

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

        await Task.WhenAll(_sqlServerFixture.DisposeAsync(), _externalServer.DisposeAsync().AsTask());
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
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        return request;
    }

    private HttpRequestMessage BuildRequestWithBody(HttpMethod method, string url, string token, object body)
    {
        var request = BuildRequest(method, url, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<Guid> CrearConectorAsync(
        string token, string codigo, TipoAutenticacionConector tipoAutenticacion = TipoAutenticacionConector.Ninguna,
        string? secretKey = null, string? apiKeyHeaderName = null,
        IReadOnlyList<(string Origen, string Destino)>? mappings = null)
    {
        var response = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/conectores", token,
            new
            {
                Codigo = codigo,
                Nombre = $"Conector {codigo}",
                BaseUrl = $"{_externalServer.BaseUrl}/webhook",
                Metodo = MetodoHttpConector.Post,
                TipoAutenticacion = tipoAutenticacion,
                SecretKey = secretKey,
                ApiKeyHeaderName = apiKeyHeaderName,
                Mappings = (mappings ?? [("cliente.nombre", "customerName")])
                    .Select(m => new { CampoOrigen = m.Origen, CampoDestino = m.Destino }).ToArray(),
            }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task ProcesarPendientesAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationHubDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IIntegrationConnectorSender>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IntegrationHubOptions>>();

        var job = new IntegrationOutboundProcessorJob(dbContext, sender, options);
        await job.ProcesarPendientesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ObtenerSolicitud_SinAutenticacion_Retorna401()
    {
        var response = await _client!.GetAsync($"/api/v1/integrationhub/solicitudes/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CrearConector_ConCodigoDuplicado_Retorna409()
    {
        var token = await SeedActorAsync("admin-duplicado", TodosLosPermisos);

        await CrearConectorAsync(token, "crm-duplicado");

        var segundaRespuesta = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/conectores", token,
            new
            {
                Codigo = "crm-duplicado",
                Nombre = "Otro nombre",
                BaseUrl = $"{_externalServer.BaseUrl}/webhook",
                Metodo = MetodoHttpConector.Post,
                TipoAutenticacion = TipoAutenticacionConector.Ninguna,
                SecretKey = (string?)null,
                ApiKeyHeaderName = (string?)null,
                Mappings = Array.Empty<object>(),
            }));

        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Corrige el hallazgo Crítico de auditoría de arquitectura (2026-09-09): el índice único de
    /// <c>IntegrationConnector.Codigo</c> no estaba compuesto con <c>TenantId</c>, así que dos tenants
    /// distintos no podían usar el mismo código de conector aunque el filtro global de EF Core hiciera
    /// que ninguno viera el conector del otro -- un Tenant B intentando crear "crm-clientes" recibía una
    /// <c>DbUpdateException</c> sin traducir si el Tenant A ya lo tenía, en vez de un 201 legítimo.</summary>
    [Fact]
    public async Task CrearConector_ConMismoCodigoEnOtroTenant_Retorna201()
    {
        var otroTenantId = Guid.NewGuid();
        var tokenTenantA = await SeedActorAsync("admin-tenant-a", TodosLosPermisos);
        var tokenTenantB = await SeedActorAsync("admin-tenant-b", TodosLosPermisos, otroTenantId);

        await CrearConectorAsync(tokenTenantA, "crm-compartido-entre-tenants");

        var respuestaTenantB = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/conectores", tokenTenantB,
            new
            {
                Codigo = "crm-compartido-entre-tenants",
                Nombre = "Conector del Tenant B",
                BaseUrl = $"{_externalServer.BaseUrl}/webhook",
                Metodo = MetodoHttpConector.Post,
                TipoAutenticacion = TipoAutenticacionConector.Ninguna,
                SecretKey = (string?)null,
                ApiKeyHeaderName = (string?)null,
                Mappings = Array.Empty<object>(),
            }));

        respuestaTenantB.StatusCode.Should().Be(HttpStatusCode.Created, await respuestaTenantB.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CrearConector_ConApiKeySinSecretKey_Retorna400()
    {
        var token = await SeedActorAsync("admin-sin-secreto", TodosLosPermisos);

        var response = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/conectores", token,
            new
            {
                Codigo = "conector-sin-secreto",
                Nombre = "Conector sin secreto",
                BaseUrl = $"{_externalServer.BaseUrl}/webhook",
                Metodo = MetodoHttpConector.Post,
                TipoAutenticacion = TipoAutenticacionConector.ApiKey,
                SecretKey = (string?)null,
                ApiKeyHeaderName = (string?)null,
                Mappings = Array.Empty<object>(),
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnviarSolicitud_ConConectorDesactivado_Retorna404()
    {
        var token = await SeedActorAsync("admin-desactivado", TodosLosPermisos);
        var conectorId = await CrearConectorAsync(token, "conector-desactivado");

        var desactivarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/integrationhub/conectores/{conectorId}/desactivar", token));
        desactivarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/solicitudes/enviar", token,
            new { ConectorCodigo = "conector-desactivado", PayloadJson = "{}" }));

        enviarResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProcesarPendientes_CaminoFeliz_AplicaMappingYLlegaAlServidorHttpReal()
    {
        var token = await SeedActorAsync("admin-camino-feliz", TodosLosPermisos);
        await CrearConectorAsync(token, "crm-camino-feliz");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/solicitudes/enviar", token,
            new { ConectorCodigo = "crm-camino-feliz", PayloadJson = """{"cliente":{"nombre":"Ana","email":"ana@test.local"}}""" }));
        enviarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var solicitudId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        await ProcesarPendientesAsync();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudId}", token));
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<IntegrationRequestResponseDto>();

        detalle!.Estado.Should().Be((int)IntegrationRequestEstado.Enviada);
        detalle.PayloadExternoJson.Should().Be("""{"customerName":"Ana"}""");

        _externalServer.Recibidas.Should().ContainSingle(r => r.Body.Contains("Ana") && r.Path == "/webhook");

        var logsResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudId}/logs", token));
        var logs = await logsResponse.Content.ReadFromJsonAsync<List<IntegrationRequestLogResponseDto>>();
        logs.Should().ContainSingle(l => l.Resultado == (int)IntegrationRequestLogResultado.Exitoso);
    }

    [Fact]
    public async Task ProcesarPendientes_ConAutenticacionApiKey_EnviaElHeaderConElSecretoResuelto()
    {
        var token = await SeedActorAsync("admin-api-key", TodosLosPermisos);
        await CrearConectorAsync(
            token, "crm-api-key", TipoAutenticacionConector.ApiKey, "conector-api-key-test", "X-Api-Key");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/solicitudes/enviar", token,
            new { ConectorCodigo = "crm-api-key", PayloadJson = """{"cliente":{"nombre":"Beto"}}""" }));
        var solicitudId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        await ProcesarPendientesAsync();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudId}", token));
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<IntegrationRequestResponseDto>();
        detalle!.Estado.Should().Be((int)IntegrationRequestEstado.Enviada, detalle.UltimoErrorMensaje);

        _externalServer.Recibidas.Count(TieneApiKeyEsperada).Should().Be(1);
    }

    private static bool TieneApiKeyEsperada(RequestRecibido r) =>
        r.Headers.TryGetValue("X-Api-Key", out var valor) && valor == "el-secreto-real-del-conector";

    /// <summary>Corrige el hallazgo Alto de auditoría de arquitectura (2026-09-09): un
    /// <c>ApiKeyHeaderName</c> con sintaxis de encabezado HTTP inválida (solo se valida
    /// <c>MaximumLength(128)</c> en el alta, nunca su formato) hacía que
    /// <c>HttpHeaders.Add</c> lanzara <c>FormatException</c> -- sin un <c>catch</c> dedicado, esa
    /// excepción caía en el catch-all genérico y se clasificaba como fallo TRANSITORIO, así que
    /// <c>IntegrationOutboundProcessorJob</c> reintentaría indefinidamente una solicitud contra un
    /// conector que nunca va a poder enviar nada. Ahora se clasifica como fallo PERMANENTE
    /// (error de configuración, no de red) y la solicitud queda <c>Fallida</c> tras un único intento.</summary>
    [Fact]
    public async Task ProcesarPendientes_ConNombreDeHeaderDeApiKeyInvalido_QuedaFallidaSinReintentarIndefinidamente()
    {
        var token = await SeedActorAsync("admin-header-invalido", TodosLosPermisos);
        // "conector-api-key-test" es la misma clave de secreto que ya usa
        // ProcesarPendientes_ConAutenticacionApiKey_EnviaElHeaderConElSecretoResuelto (el proveedor de
        // secretos del host de test la resuelve a un valor real) -- este test solo varía
        // ApiKeyHeaderName para ejercer el camino de header inválido, sin confundirlo con un fallo de
        // resolución de secreto (causa real de la primera versión fallida de este test).
        await CrearConectorAsync(
            token, "crm-header-invalido", TipoAutenticacionConector.ApiKey, "conector-api-key-test", "Nombre Con Espacios Invalido");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/solicitudes/enviar", token,
            new { ConectorCodigo = "crm-header-invalido", PayloadJson = "{}" }));
        var solicitudId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        await ProcesarPendientesAsync();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudId}", token));
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<IntegrationRequestResponseDto>();
        detalle!.Estado.Should().Be((int)IntegrationRequestEstado.Fallida);
        detalle.IntentosRealizados.Should().Be(1);
    }

    [Fact]
    public async Task ProcesarPendientes_ConCampoOrigenFaltante_OmiteElCampoEnElPayloadExterno()
    {
        var token = await SeedActorAsync("admin-campo-faltante", TodosLosPermisos);
        await CrearConectorAsync(token, "crm-campo-faltante");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/solicitudes/enviar", token,
            new { ConectorCodigo = "crm-campo-faltante", PayloadJson = """{"otroDato":"x"}""" }));
        var solicitudId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        await ProcesarPendientesAsync();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudId}", token));
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<IntegrationRequestResponseDto>();

        detalle!.Estado.Should().Be((int)IntegrationRequestEstado.Enviada);
        detalle.PayloadExternoJson.Should().Be("{}");
    }

    [Fact]
    public async Task ProcesarPendientes_ConFalloTransitorioYLuegoExito_QuedaEnviadaTrasReintento()
    {
        var token = await SeedActorAsync("admin-retry", TodosLosPermisos);
        await CrearConectorAsync(token, "crm-retry");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/solicitudes/enviar", token,
            new { ConectorCodigo = "crm-retry", PayloadJson = """{"cliente":{"nombre":"Carla"}}""" }));
        var solicitudId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        _externalServer.EncolarRespuesta(HttpStatusCode.ServiceUnavailable);

        // Primer ciclo: falla transitoriamente (503) y queda PendienteDeReintento con backoff = 0
        // (configurado en InitializeAsync) -- el segundo ciclo la reclama inmediatamente.
        await ProcesarPendientesAsync();

        var detalleTrasPrimerIntento = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudId}", token)))
            .Content.ReadFromJsonAsync<IntegrationRequestResponseDto>();
        detalleTrasPrimerIntento!.Estado.Should().Be((int)IntegrationRequestEstado.PendienteDeReintento);

        await ProcesarPendientesAsync();

        var detalleFinal = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudId}", token)))
            .Content.ReadFromJsonAsync<IntegrationRequestResponseDto>();
        detalleFinal!.Estado.Should().Be((int)IntegrationRequestEstado.Enviada);
        detalleFinal.IntentosRealizados.Should().Be(1);

        _externalServer.Recibidas.Should().HaveCount(2);
    }

    /// <summary>Corrige el mismo tipo de hallazgo que ya corrigió Notifications (Fase 6, módulo 8,
    /// FormatException de MailMessage fuera del try): un <c>BaseUrl</c> corrupto (no un caso alcanzable
    /// desde el endpoint hoy, que valida el formato en el alta, pero sí desde un dato preexistente/
    /// migrado) no debe escapar como excepción no controlada -- debe clasificarse como fallo permanente
    /// SIN perder el progreso del resto del lote.</summary>
    [Fact]
    public async Task ProcesarPendientes_ConBaseUrlCorrupta_QuedaFallidaSinExcepcionYSinPerderElRestoDelLote()
    {
        var token = await SeedActorAsync("admin-url-corrupta", TodosLosPermisos);
        await CrearConectorAsync(token, "crm-url-valida");

        await using var scope = _factory!.Services.CreateAsyncScope();

        // El claim de tenant debe estar en el HttpContext ANTES de resolver IntegrationHubDbContext:
        // MultiTenantDbContext congela _tenantId = tenantProvider.TenantId en su CONSTRUCTOR (mismo
        // motivo documentado en NotificationsEndpointsIntegrationTests).
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(TenantClaimTypes.TenantId, TenantId.ToString()));
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var connectorRepository = scope.ServiceProvider.GetRequiredService<IRepository<IntegrationConnector, Guid>>();
        var requestRepository = scope.ServiceProvider.GetRequiredService<IRepository<IntegrationRequest, Guid>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationHubDbContext>();

        // Simula un dato preexistente corrupto -- construido directamente contra el repositorio
        // (bypasseando el validador del comando HTTP, que sí rechazaría esta URL) para poder ejercer el
        // camino de manejo de excepción de HttpIntegrationConnectorSender.
        var conectorCorrupto = new IntegrationConnector(
            Guid.NewGuid(), "crm-url-corrupta", "Conector con URL corrupta", "no-es-una-url-absoluta",
            MetodoHttpConector.Post, TipoAutenticacionConector.Ninguna, secretKey: null, apiKeyHeaderName: null);
        await connectorRepository.AddAsync(conectorCorrupto);

        var solicitudConUrlCorrupta = new IntegrationRequest(Guid.NewGuid(), conectorCorrupto.Id, "{}", disparadoPorUserId: null);
        await requestRepository.AddAsync(solicitudConUrlCorrupta);
        await dbContext.SaveChangesAsync();

        var enviarValidaResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/integrationhub/solicitudes/enviar", token,
            new { ConectorCodigo = "crm-url-valida", PayloadJson = """{"cliente":{"nombre":"Dana"}}""" }));
        var solicitudValidaId = await enviarValidaResponse.Content.ReadFromJsonAsync<Guid>();

        await ProcesarPendientesAsync();

        // dbContext.IntegrationRequests.FindAsync devolvería la instancia YA TRACKEADA en memoria por
        // ESTE MISMO DbContext (identity map de EF Core) -- la que se agregó más arriba vía
        // requestRepository.AddAsync, con su estado ORIGINAL en memoria -- en vez de consultar la base,
        // que es donde ProcesarPendientesAsync() (un DbContext DISTINTO, de otro scope) realmente
        // persistió la actualización. Forzar la recarga desde la base evita leer ese dato obsoleto.
        await dbContext.Entry(solicitudConUrlCorrupta).ReloadAsync();
        solicitudConUrlCorrupta.Estado.Should().Be(IntegrationRequestEstado.Fallida);

        var validaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/integrationhub/solicitudes/{solicitudValidaId}", token));
        var validaDetalle = await validaResponse.Content.ReadFromJsonAsync<IntegrationRequestResponseDto>();
        validaDetalle!.Estado.Should().Be((int)IntegrationRequestEstado.Enviada);
    }

    private sealed record IntegrationRequestResponseDto(
        Guid Id, Guid ConnectorId, Guid? DisparadoPorUserId, string PayloadInternoJson, string? PayloadExternoJson,
        int Estado, int IntentosRealizados, string? UltimoErrorMensaje, int? UltimoCodigoHttp, DateTime? EnviadaAtUtc);

    private sealed record IntegrationRequestLogResponseDto(
        Guid Id, int IntentoNumero, DateTime TimestampUtc, int Resultado, int? CodigoHttp, string? ErrorMensaje);
}
