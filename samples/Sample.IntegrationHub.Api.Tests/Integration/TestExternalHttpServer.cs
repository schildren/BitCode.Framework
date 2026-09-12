using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sample.IntegrationHub.Api.Tests.Integration;

/// <summary>
/// Servidor HTTP REAL (Kestrel real, bindeado a un puerto de loopback dinámico dentro del mismo proceso
/// de test) que actúa como "sistema externo" que recibe las llamadas salientes de
/// <c>HttpIntegrationConnectorSender</c> -- NO un mock de <c>HttpClient</c>/<c>HttpMessageHandler</c>,
/// mismo criterio ya establecido en esta sesión para Documents (escáner de virus real con firma EICAR) y
/// Notifications (servidor SMTP real, smtp4dev). Registra cada request recibido (para que los tests
/// verifiquen el payload externo/headers realmente enviados) y permite encolar respuestas HTTP concretas
/// para simular fallos transitorios/permanentes del lado del conector.
/// </summary>
public sealed class TestExternalHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<HttpStatusCode> _respuestasEncoladas = new();

    public ConcurrentBag<RequestRecibido> Recibidas { get; } = new();

    public string BaseUrl { get; private set; } = string.Empty;

    public TestExternalHttpServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.MapMethods("/{**catchAll}", ["POST", "PUT", "PATCH"], HandleAsync);
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();

        var addressesFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        BaseUrl = addressesFeature!.Addresses.First();
    }

    /// <summary>Encola el código de estado HTTP que la PRÓXIMA request recibida devolverá -- si la cola
    /// está vacía, se responde 200 OK por defecto.</summary>
    public void EncolarRespuesta(HttpStatusCode statusCode) => _respuestasEncoladas.Enqueue(statusCode);

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        Recibidas.Add(new RequestRecibido(
            context.Request.Path.Value ?? string.Empty,
            body,
            context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())));

        context.Response.StatusCode = _respuestasEncoladas.TryDequeue(out var status) ? (int)status : StatusCodes.Status200OK;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed record RequestRecibido(string Path, string Body, IReadOnlyDictionary<string, string> Headers);
