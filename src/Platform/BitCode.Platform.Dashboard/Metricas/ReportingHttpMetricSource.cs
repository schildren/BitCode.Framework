using System.Net.Http.Json;
using System.Text.Json;

namespace BitCode.Framework.Platform.Dashboard.Metricas;

/// <summary>Contrato JSON de <c>GET /api/v1/reporting/workflow-instancias/promedio-duracion</c> (Fase 6,
/// módulo 11) -- una copia PROPIA del contrato HTTP público de Reporting, nunca una referencia al tipo
/// interno <c>PromedioDuracionPorDefinicionResponse</c> de <c>BitCode.Platform.Reporting</c> (este módulo
/// no referencia ese ensamblado en absoluto, ver el <c>remarks</c> del <c>csproj</c> de este proyecto).
/// </summary>
internal sealed record ReportingPromedioDuracionDto(
    Guid WorkflowDefinitionId, int CantidadInstanciasFinalizadas, double PromedioDuracionSegundos);

/// <summary>
/// Implementación de referencia REAL (no un mock/fake) de <see cref="IReportingMetricSource"/> -- llama a
/// la API HTTP pública de Reporting. Funciona contra cualquier servidor HTTP real que exponga ese
/// contrato (incluido uno de prueba embebido en el mismo proceso, ver <c>Sample.Dashboard.Api.Tests</c>),
/// no un stub.
/// </summary>
internal sealed class ReportingHttpMetricSource(DashboardReportingHttpClient client) : IReportingMetricSource
{
    public async Task<ReportingMetricResultado> ObtenerPromedioDuracionPorDefinicionAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/v1/reporting/workflow-instancias/promedio-duracion?workflowDefinitionId={workflowDefinitionId:D}";
            using var response = await client.HttpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 4xx/5xx de Reporting -- clasificado como "no disponible" en vez de propagar el código
                // HTTP del sistema externo tal cual: el dashboard es un agregador de MUCHOS widgets
                // potencialmente contra MUCHAS fuentes en el futuro, así que un único widget fallando
                // nunca debe tumbar la respuesta completa del dashboard (ver
                // ObtenerMetricaWidgetQueryHandler).
                return ReportingMetricResultado.NoDisponible(
                    $"Reporting respondió {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var filas = await response.Content.ReadFromJsonAsync<List<ReportingPromedioDuracionDto>>(cancellationToken)
                ?? [];

            var fila = filas.FirstOrDefault(f => f.WorkflowDefinitionId == workflowDefinitionId);

            // Ausencia de fila = "todavía no hay ninguna instancia finalizada para esta definición", un
            // resultado de negocio legítimo (Reporting solo agrega instancias YA finalizadas, ver
            // docs/guia-reporting.md) -- NO es un fallo de la llamada, así que se reporta Disponible con
            // el promedio en null, nunca NoDisponible.
            return fila is null
                ? ReportingMetricResultado.Disponible(promedioDuracionSegundos: null, cantidadInstanciasFinalizadas: 0)
                : ReportingMetricResultado.Disponible(fila.PromedioDuracionSegundos, fila.CantidadInstanciasFinalizadas);
        }
        catch (JsonException ex)
        {
            // Respuesta de Reporting con un formato inesperado (contrato roto, HTML de un proxy/gateway
            // caído, etc.) -- no se puede interpretar, pero NUNCA debe escapar como excepción no
            // controlada (mismo hallazgo Alto ya corregido por Notifications/Integration Hub para
            // FormatException/UriFormatException fuera de un catch dedicado).
            return ReportingMetricResultado.NoDisponible($"Respuesta de Reporting no interpretable: {ex.Message}");
        }
        catch (UriFormatException ex)
        {
            // BaseAddress mal configurado (Dashboard:ReportingBaseUrl) -- error de configuración, no de
            // red, pero igual se clasifica como "no disponible" (nunca corrige la configuración solo, y
            // el widget individual no debe tumbar el resto del dashboard).
            return ReportingMetricResultado.NoDisponible($"URL de Reporting inválida: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cualquier otro fallo (host no responde / DNS / conexión rechazada / la pipeline de
            // resiliencia F1-26 agotó reintentos/circuit breaker abierto) se trata como métrica no
            // disponible -- mismo criterio conservador que HttpIntegrationConnectorSender.
            return ReportingMetricResultado.NoDisponible(ex.Message);
        }
    }
}
