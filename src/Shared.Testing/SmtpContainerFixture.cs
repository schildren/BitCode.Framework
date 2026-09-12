using System.Net.Http.Headers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BitCode.Framework.Shared.Testing;

/// <summary>
/// Fixture de xUnit reutilizable para pruebas de integración del módulo Notifications (Fase 6, módulo 8)
/// contra un servidor SMTP REAL mediante Testcontainers -- mismo espíritu que
/// <see cref="VaultContainerFixture"/> (usa el builder genérico de Testcontainers en vez de un paquete
/// dedicado, que no existe en el ecosistema .NET para <c>rnwood/smtp4dev</c>). <c>smtp4dev</c> es un
/// servidor SMTP de pruebas real (acepta conexiones SMTP reales, no un mock en memoria) con una API REST
/// para consultar los mensajes efectivamente recibidos (<see cref="GetReceivedMessagesRawJsonAsync"/>) --
/// permite verificar de punta a punta que <c>EmailNotificationChannelSender</c> entregó un email real,
/// sin depender de ningún proveedor SMTP comercial ni de credenciales.
/// </summary>
public sealed class SmtpContainerFixture : IAsyncLifetime
{
    private const int SmtpContainerPort = 25;
    private const int ApiContainerPort = 80;

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("rnwood/smtp4dev:v3")
        .WithPortBinding(SmtpContainerPort, assignRandomHostPort: true)
        .WithPortBinding(ApiContainerPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(ApiContainerPort).ForPath("/api/messages"))
            // La API REST (puerto 80) puede quedar lista antes que el listener SMTP (puerto 25) --
            // ambas esperas evitan una carrera donde EmailNotificationChannelSender intenta conectarse
            // al puerto SMTP antes de que smtp4dev lo tenga escuchando.
            .UntilPortIsAvailable(SmtpContainerPort))
        .Build();

    public string SmtpHost => _container.Hostname;

    public int SmtpPort => _container.GetMappedPublicPort(SmtpContainerPort);

    private string ApiBaseUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ApiContainerPort)}";

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Devuelve el JSON crudo de <c>GET /api/messages</c> de smtp4dev (lista de mensajes recibidos hasta
    /// el momento). DELIBERADAMENTE no se deserializa a un tipo fuertemente tipado -- el esquema exacto
    /// de la API REST de smtp4dev no es un contrato estable de este framework; un test que necesita
    /// verificar "llegó un email con este asunto/destinatario" alcanza con <c>string.Contains</c> sobre
    /// este JSON, sin acoplarse a la forma completa de la respuesta.
    /// </summary>
    public async Task<string> GetReceivedMessagesRawJsonAsync(CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await httpClient.GetStringAsync($"{ApiBaseUrl}/api/messages", cancellationToken);
    }
}
