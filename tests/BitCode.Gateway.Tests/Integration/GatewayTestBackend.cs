using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// Backend HTTP real (Kestrel, puerto dinámico) que actúa como destino del cluster YARP del Gateway
/// bajo prueba -- YARP proxya con un HttpMessageInvoker real hacia la red, así que el destino no puede
/// ser un <c>TestServer</c> en memoria (mismo patrón recomendado por la documentación de pruebas de
/// integración de YARP). Expone un endpoint que devuelve, como JSON, los headers recibidos -- usado
/// para verificar routing (F4-08, "al menos un route/cluster funcional") y forwarding de headers.
/// </summary>
public sealed class GatewayTestBackend : IAsyncDisposable
{
    private WebApplication? _app;

    public string BaseAddress { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapGet("/api/echo", (HttpRequest request) =>
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
            return Results.Ok(headers);
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
