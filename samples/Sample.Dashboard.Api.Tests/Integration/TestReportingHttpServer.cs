using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sample.Dashboard.Api.Tests.Integration;

/// <summary>
/// Servidor HTTP REAL (Kestrel real, bindeado a un puerto de loopback dinámico dentro del mismo proceso de
/// test) que actúa como "Reporting" (Fase 6, módulo 11) -- expone el MISMO contrato que
/// <c>GET /api/v1/reporting/workflow-instancias/promedio-duracion</c> -- NO un mock de
/// <c>HttpClient</c>/<c>HttpMessageHandler</c>, mismo criterio ya establecido en esta sesión por
/// <c>Sample.IntegrationHub.Api.Tests.TestExternalHttpServer</c>.
/// </summary>
public sealed class TestReportingHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<Guid, (int Cantidad, double Promedio)> _datos = new();
    private HttpStatusCode? _statusCodeForzado;
    private bool _respuestaCorrupta;

    public string BaseUrl { get; private set; } = string.Empty;

    public TestReportingHttpServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.MapGet("/api/v1/reporting/workflow-instancias/promedio-duracion", HandleAsync);
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();

        var addressesFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        BaseUrl = addressesFeature!.Addresses.First();
    }

    /// <summary>Registra el dato que "Reporting" devolverá para una definición de workflow concreta --
    /// simula que ya hay instancias finalizadas para esa definición.</summary>
    public void ConfigurarDato(Guid workflowDefinitionId, int cantidadInstanciasFinalizadas, double promedioDuracionSegundos) =>
        _datos[workflowDefinitionId] = (cantidadInstanciasFinalizadas, promedioDuracionSegundos);

    /// <summary>Simula que Reporting está caído/sobrecargado -- todas las respuestas siguientes usan este
    /// código de estado.</summary>
    public void ForzarRespuesta(HttpStatusCode statusCode) => _statusCodeForzado = statusCode;

    /// <summary>Simula una respuesta con un formato que no se puede interpretar (contrato roto/HTML de un
    /// proxy caído) -- ejerce el catch de <c>JsonException</c> de <c>ReportingHttpMetricSource</c>.</summary>
    public void ForzarRespuestaCorrupta() => _respuestaCorrupta = true;

    private async Task HandleAsync(HttpContext context)
    {
        if (_statusCodeForzado is not null)
        {
            context.Response.StatusCode = (int)_statusCodeForzado.Value;
            return;
        }

        if (_respuestaCorrupta)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("esto-no-es-json-valido {{{");
            return;
        }

        var filas = new List<object>();
        if (Guid.TryParse(context.Request.Query["workflowDefinitionId"].ToString(), out var workflowDefinitionId) &&
            _datos.TryGetValue(workflowDefinitionId, out var dato))
        {
            filas.Add(new
            {
                WorkflowDefinitionId = workflowDefinitionId,
                CantidadInstanciasFinalizadas = dato.Cantidad,
                PromedioDuracionSegundos = dato.Promedio,
            });
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(filas));
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
