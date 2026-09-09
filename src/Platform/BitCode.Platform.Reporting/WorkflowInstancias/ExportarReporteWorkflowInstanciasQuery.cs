using System.Text;
using BitCode.Framework.Platform.Reporting.Csv;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

/// <summary>
/// Exporta a CSV el resultado COMPLETO de los mismos filtros que
/// <see cref="ListarReporteWorkflowInstanciasQuery"/> (Fase 6, módulo 11: "exportación" del Plan Maestro) --
/// la consulta en pantalla queda paginada, pero la exportación siempre trae todas las filas que cumplen el
/// filtro, sin paginar.
/// </summary>
/// <remarks>
/// <b>Limitación honesta (ver <c>docs/guia-reporting.md</c>, "Qué quedó completo y qué no"):</b> a
/// diferencia de <c>ExportBatchProcessorJob</c> (Fase 6, módulo 10 — Import and Export), que procesa la
/// exportación en chunks vía un job en background con checkpoint/reanudación, esta exportación es
/// SINCRÓNICA: trae todas las filas que cumplen el filtro a memoria de una sola vez
/// (<c>IReadRepository.ListAsync(spec, selector)</c>) y arma el CSV completo antes de responder. Aceptable
/// para el volumen esperado de este read-model (miles de instancias, no millones) pero no apto, sin
/// revisión, para un tenant con un volumen de instancias de Workflow varios órdenes de magnitud mayor — un
/// consumidor real con ese volumen debería migrar a un mecanismo de export en background (reutilizando el
/// patrón, no el código, de Import and Export) en vez de este endpoint síncrono.
/// </remarks>
internal sealed record ExportarReporteWorkflowInstanciasQuery(
    Guid? WorkflowDefinitionId, ReporteWorkflowInstanciaEstado? Estado, DateTime? DesdeUtc, DateTime? HastaUtc)
    : IQuery<ReporteWorkflowInstanciaDescargaResponse>;

internal sealed class ExportarReporteWorkflowInstanciasQueryHandler(
    IReadRepository<ReporteWorkflowInstancia, Guid> repository)
    : IRequestHandler<ExportarReporteWorkflowInstanciasQuery, Result<ReporteWorkflowInstanciaDescargaResponse>>
{
    private static readonly string[] Columnas =
    [
        "WorkflowInstanceId", "WorkflowDefinitionId", "Estado", "IniciadaAtUtc", "FinalizadaAtUtc",
        "DuracionSegundos", "EstadoFinalId",
    ];

    public async Task<Result<ReporteWorkflowInstanciaDescargaResponse>> Handle(
        ExportarReporteWorkflowInstanciasQuery request, CancellationToken cancellationToken)
    {
        var specification = new ReporteWorkflowInstanciaFiltroSpecification(
            request.WorkflowDefinitionId, request.Estado, request.DesdeUtc, request.HastaUtc);

        var filas = await repository.ListAsync(
            specification,
            r => new ReporteWorkflowInstanciaResponse(
                r.Id, r.WorkflowDefinitionId, r.Estado, r.IniciadaAtUtc, r.FinalizadaAtUtc, r.DuracionSegundos, r.EstadoFinalId),
            cancellationToken);

        var csv = new StringBuilder();
        csv.Append(ReportingCsvWriter.WriteLine(Columnas));

        foreach (var fila in filas)
        {
            try
            {
                csv.Append(ReportingCsvWriter.WriteLine(
                [
                    fila.Id.ToString(),
                    fila.WorkflowDefinitionId.ToString(),
                    fila.Estado.ToString(),
                    fila.IniciadaAtUtc?.ToString("O"),
                    fila.FinalizadaAtUtc?.ToString("O"),
                    fila.DuracionSegundos?.ToString(),
                    fila.EstadoFinalId?.ToString(),
                ]));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Defensivo (regla dura de esta tarea: nunca asumir ciegamente que serializar una fila a
                // CSV no puede fallar, mismo criterio que ExportBatchProcessorJob de Import and Export) --
                // una fila que no se pudo serializar simplemente se omite del archivo en vez de tumbar toda
                // la exportación.
                continue;
            }
        }

        var contenido = Encoding.UTF8.GetBytes(csv.ToString());
        return Result.Success(new ReporteWorkflowInstanciaDescargaResponse(
            contenido, "text/csv", $"reporte-workflow-instancias-{DateTime.UtcNow:yyyyMMddHHmmss}.csv"));
    }
}
