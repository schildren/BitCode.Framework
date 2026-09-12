using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// F9-09 (Fase 9, "Resiliencia", <c>docs/guia-workflow.md</c> sección "Resiliencia (F9-09)") -- backend
/// HTTP real (Kestrel, puerto dinámico) que representa al host independiente de Workflow (F9-05)
/// respondiendo con latencia artificial en el mismo prefijo real que expone
/// <c>WorkflowEndpointRouteBuilderExtensions</c> (<c>/api/v1/workflows/...</c>). Mismo patrón que
/// <see cref="GatewayWorkflowRoutingTestBackend"/>, con la diferencia de que este backend introduce un
/// retraso controlado antes de responder -- usado para verificar, contra un timeout de YARP realmente
/// configurado, que el Gateway corta la espera en vez de colgarse indefinidamente.
/// </summary>
public sealed class GatewayWorkflowSlowTestBackend(TimeSpan delay) : IAsyncDisposable
{
    private WebApplication? _app;

    public string BaseAddress { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapGet("/api/v1/workflows/{**catchAll}", async (CancellationToken cancellationToken) =>
        {
            // El backend intencionalmente NO respeta el cancellationToken del lado servidor con un
            // Task.Delay que ignore la cancelación temprana -- así el retraso configurado siempre se
            // agota del lado del backend, y es el Gateway (o el cliente) quien debe cortar la espera
            // primero si tiene un timeout configurado. Esto evita que el test dé un falso positivo por
            // el backend cortando la conexión por su cuenta.
            await Task.Delay(delay, CancellationToken.None);
            return Results.Ok(new { backend = "sample-workflow-api-lento" });
        });

        await _app.StartAsync();

        BaseAddress = _app.Urls.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
