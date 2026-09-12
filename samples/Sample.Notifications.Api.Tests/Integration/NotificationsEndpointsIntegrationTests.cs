using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BitCode.Framework.Platform.Notifications;
using BitCode.Framework.Platform.Notifications.Envio;
using BitCode.Framework.Platform.Notifications.Envio.Canales;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Platform.Notifications.Retry;
using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Application.Inbox;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sample.Notifications.Api.Tests.Integration;

/// <summary>
/// Verifica, contra SQL Server real (Testcontainers) y un servidor SMTP real (smtp4dev, Testcontainers),
/// el módulo Notifications (Fase 6, módulo 8): plantillas, canales (Email real vía SMTP, InApp), la
/// preferencia de opt-out respetada ANTES de intentar entregar, retry con backoff (F3-07 reutilizado) y
/// tracking de cada intento, además del consumidor de EJEMPLO que engancha
/// <c>Workflow.TareaAsignada</c> (Fase 6, módulo 6) sin depender de <c>WorkflowDbContext</c>.
/// </summary>
/// <remarks>
/// Mismo criterio que <c>TaskInboxEndpointsIntegrationTests</c>: este host NO tiene Workflow corriendo en
/// el mismo proceso (ver <c>InfrastructureModule</c>) -- el evento de Workflow que
/// <c>TareaAsignadaNotificationEventConsumer</c> consume se SIMULA construyendo directamente su
/// <c>record</c> público e invocando el mismo mecanismo que un <c>KafkaEventConsumer&lt;TEvent&gt;</c>
/// real usaría (<c>IInboxMessageProcessor.ProcessAsync</c>).
/// </remarks>
public class NotificationsEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private readonly SmtpContainerFixture _smtpFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly IReadOnlyList<string> TodosLosPermisos =
    [
        NotificationsPermissions.PlantillasAdministrar,
        NotificationsPermissions.NotificacionesEnviar,
        NotificationsPermissions.NotificacionesVer,
        NotificationsPermissions.NotificacionesMarcarLeida,
        NotificationsPermissions.PreferenciasAdministrar,
    ];

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sqlServerFixture.InitializeAsync(), _smtpFixture.InitializeAsync());

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleNotificationsApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleNotificationsApiTestsIdentity", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("Notifications__Smtp__Host", _smtpFixture.SmtpHost);
        Environment.SetEnvironmentVariable("Notifications__Smtp__Port", _smtpFixture.SmtpPort.ToString());
        Environment.SetEnvironmentVariable("Notifications__Retry__MaxAttempts", "2");
        Environment.SetEnvironmentVariable("Notifications__Retry__BaseDelay", "00:00:00");
        Environment.SetEnvironmentVariable("Notifications__Retry__MaxDelay", "00:00:00");

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

        await Task.WhenAll(_sqlServerFixture.DisposeAsync(), _smtpFixture.DisposeAsync());
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

    private async Task<Guid> CrearPlantillaAsync(
        string token, string codigo, NotificationChannel canal, string? asunto, string cuerpo, string locale = "es-AR")
    {
        var response = await _client!.SendAsync(
            BuildRequestWithBody(HttpMethod.Post, "/api/v1/notifications/plantillas", token,
                new { Codigo = codigo, Canal = canal, Locale = locale, Asunto = asunto, Cuerpo = cuerpo }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private HttpRequestMessage BuildRequestWithBody(HttpMethod method, string url, string token, object body)
    {
        var request = BuildRequest(method, url, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

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

    [Fact]
    public async Task ObtenerNotificacion_SinAutenticacion_Retorna401()
    {
        var response = await _client!.GetAsync($"/api/v1/notifications/notificaciones/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnviarNotificacionInApp_CaminoFeliz_QuedaEnviadaYLegibleParaElDestinatario()
    {
        var (_, adminToken) = await SeedActorAsync("admin-inapp", TodosLosPermisos);
        var (destinatarioUserId, destinatarioToken) = await SeedActorAsync("destinatario-inapp", TodosLosPermisos);

        await CrearPlantillaAsync(adminToken, "bienvenida", NotificationChannel.InApp, asunto: null, cuerpo: "Hola {nombre}, bienvenido.");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/notifications/notificaciones/enviar", adminToken,
            new
            {
                DestinatarioUserId = destinatarioUserId,
                DestinatarioContacto = (string?)null,
                CodigoPlantilla = "bienvenida",
                Canal = NotificationChannel.InApp,
                Locale = "es-AR",
                Datos = new Dictionary<string, string> { ["nombre"] = "Ana" },
            }));
        enviarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var notificationId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/notifications/notificaciones/{notificationId}", destinatarioToken));
        detalleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<NotificationResponseDto>();

        detalle!.Estado.Should().Be((int)NotificationEstado.Enviada);
        detalle.Cuerpo.Should().Be("Hola Ana, bienvenido.");
    }

    [Fact]
    public async Task EnviarNotificacion_ConPreferenciaDeOptOut_QuedaOmitidaSinIntentarEntregar()
    {
        var (_, adminToken) = await SeedActorAsync("admin-optout", TodosLosPermisos);
        var (destinatarioUserId, destinatarioToken) = await SeedActorAsync("destinatario-optout", TodosLosPermisos);

        await CrearPlantillaAsync(adminToken, "promocional", NotificationChannel.InApp, asunto: null, cuerpo: "Oferta especial.");

        var optOutResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/notifications/preferencias/opt-out", destinatarioToken,
            new { CodigoPlantilla = "promocional", Canal = NotificationChannel.InApp }));
        optOutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/notifications/notificaciones/enviar", adminToken,
            new
            {
                DestinatarioUserId = destinatarioUserId,
                DestinatarioContacto = (string?)null,
                CodigoPlantilla = "promocional",
                Canal = NotificationChannel.InApp,
                Locale = "es-AR",
                Datos = new Dictionary<string, string>(),
            }));
        var notificationId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/notifications/notificaciones/{notificationId}", destinatarioToken));
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<NotificationResponseDto>();

        detalle!.Estado.Should().Be((int)NotificationEstado.OmitidaPorPreferencia);
    }

    /// <summary>Hallazgo Alto (IDOR) evitado desde el diseño inicial (mismo patrón que Task Inbox tuvo
    /// que corregir tras auditoría, 2026-09-09): solo el destinatario o quien disparó la notificación
    /// pueden verla por id.</summary>
    [Fact]
    public async Task ObtenerNotificacion_SinSerElDestinatarioNiQuienLaDisparo_Retorna403()
    {
        var (_, adminToken) = await SeedActorAsync("admin-idor", TodosLosPermisos);
        var (destinatarioUserId, _) = await SeedActorAsync("destinatario-idor", TodosLosPermisos);
        var (_, actorAjenoToken) = await SeedActorAsync("ajeno-idor", TodosLosPermisos);

        await CrearPlantillaAsync(adminToken, "idor-test", NotificationChannel.InApp, asunto: null, cuerpo: "Contenido sensible.");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/notifications/notificaciones/enviar", adminToken,
            new
            {
                DestinatarioUserId = destinatarioUserId,
                DestinatarioContacto = (string?)null,
                CodigoPlantilla = "idor-test",
                Canal = NotificationChannel.InApp,
                Locale = "es-AR",
                Datos = new Dictionary<string, string>(),
            }));
        var notificationId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/notifications/notificaciones/{notificationId}", actorAjenoToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MarcarComoLeida_SinSerElDestinatario_Retorna403()
    {
        var (_, adminToken) = await SeedActorAsync("admin-leida", TodosLosPermisos);
        var (destinatarioUserId, _) = await SeedActorAsync("destinatario-leida", TodosLosPermisos);
        var (_, actorAjenoToken) = await SeedActorAsync("ajeno-leida", TodosLosPermisos);

        await CrearPlantillaAsync(adminToken, "leida-test", NotificationChannel.InApp, asunto: null, cuerpo: "Contenido.");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/notifications/notificaciones/enviar", adminToken,
            new
            {
                DestinatarioUserId = destinatarioUserId,
                DestinatarioContacto = (string?)null,
                CodigoPlantilla = "leida-test",
                Canal = NotificationChannel.InApp,
                Locale = "es-AR",
                Datos = new Dictionary<string, string>(),
            }));
        var notificationId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/notifications/notificaciones/{notificationId}/marcar-leida", actorAjenoToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TareaAsignada_DisparaNotificacionInAppParaElAsignado()
    {
        var (_, adminToken) = await SeedActorAsync("admin-workflow", TodosLosPermisos);
        var (asignadoUserId, asignadoToken) = await SeedActorAsync("asignado-workflow", TodosLosPermisos);

        // "tarea-asignada" / "es-AR": mismo código lógico y locale por defecto que
        // TareaAsignadaNotificationEventConsumer usa internamente (ver
        // Eventos/TareaAsignadaNotificationEventConsumer.cs) -- constantes internas del ensamblado, no
        // accesibles desde este proyecto de test, así que se repiten acá literalmente (documentado
        // para que quede claro que deben coincidir).
        await CrearPlantillaAsync(
            adminToken, "tarea-asignada", NotificationChannel.InApp,
            asunto: null, cuerpo: "Tenés una tarea nueva: {workflowTaskId}.",
            locale: "es-AR");

        var workflowTaskId = Guid.NewGuid();
        await SimularEventoAsync(new TareaAsignadaIntegrationEvent(workflowTaskId, Guid.NewGuid(), asignadoUserId));

        var listarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, "/api/v1/notifications/notificaciones", asignadoToken));
        listarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagina = await listarResponse.Content.ReadFromJsonAsync<PagedResultDto<NotificationResponseDto>>();

        pagina!.Items.Should()
            .ContainSingle(n => n.Estado == (int)NotificationEstado.Enviada && n.Cuerpo.Contains(workflowTaskId.ToString()));
    }

    [Fact]
    public async Task EnviarNotificacionEmail_CaminoFeliz_LlegaAlServidorSmtpReal()
    {
        var (_, adminToken) = await SeedActorAsync("admin-email", TodosLosPermisos);
        var (destinatarioUserId, _) = await SeedActorAsync("destinatario-email", TodosLosPermisos);

        var asuntoUnico = $"Asunto de prueba {Guid.NewGuid():N}";
        await CrearPlantillaAsync(
            adminToken, "email-test", NotificationChannel.Email, asunto: asuntoUnico, cuerpo: "Cuerpo del email de prueba.");

        var enviarResponse = await _client!.SendAsync(BuildRequestWithBody(
            HttpMethod.Post, "/api/v1/notifications/notificaciones/enviar", adminToken,
            new
            {
                DestinatarioUserId = destinatarioUserId,
                DestinatarioContacto = "destinatario-real@bitcode-test.local",
                CodigoPlantilla = "email-test",
                Canal = NotificationChannel.Email,
                Locale = "es-AR",
                Datos = new Dictionary<string, string>(),
            }));
        enviarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var notificationId = await enviarResponse.Content.ReadFromJsonAsync<Guid>();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/notifications/notificaciones/{notificationId}", adminToken));
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<NotificationResponseDto>();
        detalle!.Estado.Should().Be(
            (int)NotificationEstado.Enviada,
            $"UltimoErrorMensaje: {detalle.UltimoErrorMensaje}");

        var mensajesJson = await _smtpFixture.GetReceivedMessagesRawJsonAsync();
        mensajesJson.Should().Contain(asuntoUnico);
        mensajesJson.Should().Contain("destinatario-real@bitcode-test.local");
    }

    [Fact]
    public async Task NotificationRetryJob_ReintentaUnCanalTransitoriamenteCaidoYTerminaFallida()
    {
        var (_, adminToken) = await SeedActorAsync("admin-retry", TodosLosPermisos);
        var (destinatarioUserId, _) = await SeedActorAsync("destinatario-retry", TodosLosPermisos);

        await CrearPlantillaAsync(adminToken, "retry-test", NotificationChannel.Email, asunto: "Asunto", cuerpo: "Cuerpo.");

        // Un servidor SMTP de pruebas real (smtp4dev) acepta cualquier destinatario sin validar si el
        // dominio existe -- no hay forma HONESTA de forzar un fallo TRANSITORIO real de red contra él sin
        // apagar/desconectar el contenedor a mitad de la prueba (frágil, no determinístico). Por eso este
        // test usa un <see cref="INotificationChannelSender"/> de prueba que siempre falla
        // transitoriamente -- reemplaza la lista de canales SOLO para <see cref="NotificationRetryJob"/>
        // (construido acá directamente, no resuelto del contenedor de DI), ejercitando la MISMA lógica de
        // decisión (<c>Notification.RegistrarEnvioFallidoTransitorio</c>/
        // <c>EventRetryBackoff.IsExhausted</c>, F3-07) sin depender de la semántica exacta de un servidor
        // SMTP externo.
        await using var scope = _factory!.Services.CreateAsyncScope();

        // El claim de tenant debe estar en el HttpContext ANTES de resolver NotificationsDbContext:
        // MultiTenantDbContext congela _tenantId = tenantProvider.TenantId en su CONSTRUCTOR (no lo
        // vuelve a evaluar por consulta) -- resolver el DbContext antes de fijar el HttpContext lo deja
        // con tenant "Guid.Empty" para siempre en este scope (mismo motivo por el que
        // TaskInboxEndpointsIntegrationTests.SimularEventoAsync fija el HttpContext ANTES de resolver
        // cualquier servicio que dependa, directa o indirectamente, del DbContext).
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(TenantClaimTypes.TenantId, TenantId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var retryOptions = scope.ServiceProvider.GetRequiredService<IOptions<NotificationsOptions>>();

        var siempreTransitorio = new SiempreFallaTransitoriamenteChannelSender(NotificationChannel.Email);
        var notificationRepository = scope.ServiceProvider.GetRequiredService<IRepository<Notification, Guid>>();
        var notification = new Notification(
            Guid.NewGuid(), destinatarioUserId, "contacto@ejemplo.local", disparadoPorUserId: null,
            "retry-test", NotificationChannel.Email, "es-AR", asunto: "Asunto", cuerpo: "Cuerpo.");
        await notificationRepository.AddAsync(notification);
        await dbContext.SaveChangesAsync();

        var job = new NotificationRetryJob(dbContext, [siempreTransitorio], retryOptions);

        // MaxAttempts=2 (configurado en appsettings vía variables de entorno de este test, ver
        // InitializeAsync): la notificación arranca en PendienteDeEnvio con 0 intentos -- pero
        // NotificationRetryJob solo reclama filas en PendienteDeReintento, así que se corre el job DOS
        // veces manipulando directamente el estado, simulando dos ciclos consecutivos donde el canal
        // sigue caído.
        notification.RegistrarEnvioFallidoTransitorio("fallo inicial simulado", DateTime.UtcNow, retryOptions.Value.Retry);
        await dbContext.SaveChangesAsync();
        notification.Estado.Should().Be(NotificationEstado.PendienteDeReintento);

        await job.ReintentarPendientesAsync(CancellationToken.None);

        var notificationRecargada = await dbContext.Notifications.FindAsync(notification.Id);
        notificationRecargada.Should().NotBeNull();
        notificationRecargada!.Estado.Should().Be(NotificationEstado.Fallida);
        notificationRecargada.IntentosRealizados.Should().Be(2);
    }

    /// <summary>Corrige el hallazgo Alto de auditoría de arquitectura (2026-09-09): un
    /// <c>DestinatarioContacto</c> con formato de email inválido (dato corrupto/typo, no un fallo de
    /// red) hacía que <c>EmailNotificationChannelSender</c> lanzara una <c>FormatException</c> no
    /// controlada (construcción de <c>MailMessage</c> fuera del <c>try/catch</c>), que escapaba
    /// <c>NotificationRetryJob.ReintentarPendientesAsync</c> ANTES del único <c>SaveChangesAsync</c> al
    /// final del lote -- perdiendo en silencio el progreso de TODAS las notificaciones ya procesadas en
    /// ese ciclo, no solo la del contacto inválido. Este test usa los <see cref="INotificationChannelSender"/>
    /// REALES resueltos del contenedor de DI (no el doble determinístico) para ejercitar el fix de punta
    /// a punta: la notificación de Email con contacto inválido debe terminar en
    /// <see cref="NotificationEstado.Fallida"/> (fallo permanente clasificado, sin excepción) Y la
    /// notificación InApp del mismo lote debe seguir procesándose y terminar
    /// <see cref="NotificationEstado.Enviada"/> -- antes del fix, la excepción de la primera abortaba el
    /// <c>foreach</c> y la segunda se quedaba sin persistir su resultado.</summary>
    [Fact]
    public async Task NotificationRetryJob_ConContactoDeEmailInvalido_NoPierdeElProgresoDeOtrasNotificacionesDelLote()
    {
        var (_, adminToken) = await SeedActorAsync("admin-retry-idor-email", TodosLosPermisos);
        var (destinatarioUserId, _) = await SeedActorAsync("destinatario-retry-email-invalido", TodosLosPermisos);

        await using var scope = _factory!.Services.CreateAsyncScope();

        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(TenantClaimTypes.TenantId, TenantId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var retryOptions = scope.ServiceProvider.GetRequiredService<IOptions<NotificationsOptions>>();
        var channelSendersReales = scope.ServiceProvider.GetServices<INotificationChannelSender>().ToArray();
        var notificationRepository = scope.ServiceProvider.GetRequiredService<IRepository<Notification, Guid>>();

        var notificacionEmailInvalida = new Notification(
            Guid.NewGuid(), destinatarioUserId, "esto-no-es-un-email-valido", disparadoPorUserId: null,
            "retry-email-invalido-test", NotificationChannel.Email, "es-AR", asunto: "Asunto", cuerpo: "Cuerpo.");
        var notificacionInApp = new Notification(
            Guid.NewGuid(), destinatarioUserId, destinatarioContacto: null, disparadoPorUserId: null,
            "retry-inapp-test", NotificationChannel.InApp, "es-AR", asunto: null, cuerpo: "Cuerpo InApp.");

        await notificationRepository.AddAsync(notificacionEmailInvalida);
        await notificationRepository.AddAsync(notificacionInApp);
        await dbContext.SaveChangesAsync();

        notificacionEmailInvalida.RegistrarEnvioFallidoTransitorio("fallo inicial simulado", DateTime.UtcNow, retryOptions.Value.Retry);
        notificacionInApp.RegistrarEnvioFallidoTransitorio("fallo inicial simulado", DateTime.UtcNow, retryOptions.Value.Retry);
        await dbContext.SaveChangesAsync();

        var job = new NotificationRetryJob(dbContext, channelSendersReales, retryOptions);
        await job.ReintentarPendientesAsync(CancellationToken.None);

        var emailRecargada = await dbContext.Notifications.FindAsync(notificacionEmailInvalida.Id);
        emailRecargada!.Estado.Should().Be(NotificationEstado.Fallida);

        var inAppRecargada = await dbContext.Notifications.FindAsync(notificacionInApp.Id);
        inAppRecargada!.Estado.Should().Be(NotificationEstado.Enviada);
    }

    /// <summary>Doble de prueba DETERMINÍSTICO de <see cref="INotificationChannelSender"/> -- no un mock
    /// de una librería de mocking, solo la implementación mínima necesaria para ejercitar el camino de
    /// fallo transitorio de <see cref="NotificationRetryJob"/> sin depender de la semántica exacta de un
    /// servidor SMTP externo (ver el comentario del test que lo usa).</summary>
    private sealed class SiempreFallaTransitoriamenteChannelSender(NotificationChannel canal) : INotificationChannelSender
    {
        public NotificationChannel Canal => canal;

        public Task<NotificationSendResult> SendAsync(Notification notification, CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationSendResult.FalloTransitorio("fallo simulado determinístico"));
    }

    private sealed record NotificationResponseDto(
        Guid Id, Guid DestinatarioUserId, Guid? DisparadoPorUserId, string CodigoPlantilla, int Canal, string Locale,
        string? Asunto, string Cuerpo, int Estado, int IntentosRealizados, string? UltimoErrorMensaje,
        DateTime? EnviadaAtUtc, DateTime? LeidoAtUtc);

    private sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
}
