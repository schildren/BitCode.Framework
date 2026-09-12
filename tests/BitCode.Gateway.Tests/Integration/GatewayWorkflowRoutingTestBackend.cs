using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BitCode.Gateway.Tests.Integration;

/// <summary>
/// F9-06 (Fase 9, "Routing") -- backend HTTP real (Kestrel, puerto dinámico) que representa, para
/// efectos de esta prueba, el host independiente de Workflow (<c>samples/Sample.Workflow.Api</c>, F9-05)
/// detrás del nuevo cluster <c>sample-workflow-api-cluster</c>. Mismo patrón exacto que
/// <see cref="GatewayTestBackend"/> (que ya representa a <c>sample-api</c>) -- un backend HTTP real
/// distinto, no un mock, para poder distinguir sin ambigüedad "el Gateway enrutó hacia Workflow" de
/// "el Gateway enrutó hacia sample-api" con una sola aserción sobre el cuerpo de la respuesta.
/// Mapea el mismo prefijo real que expone <c>WorkflowEndpointRouteBuilderExtensions</c>
/// (<c>/api/v1/workflows/...</c>), no un prefijo inventado para la prueba.
/// </summary>
public sealed class GatewayWorkflowRoutingTestBackend : IAsyncDisposable
{
    private WebApplication? _app;

    public string BaseAddress { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapGet("/api/v1/workflows/{**catchAll}", () => Results.Ok(new { backend = "sample-workflow-api" }));

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
