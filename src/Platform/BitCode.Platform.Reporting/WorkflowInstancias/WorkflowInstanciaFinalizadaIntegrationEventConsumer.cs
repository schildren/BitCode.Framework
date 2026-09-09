using BitCode.Framework.Platform.Workflow.Instancias;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

/// <summary>
/// Construye/actualiza el read-model propio de Reporting a partir de
/// <see cref="WorkflowInstanciaFinalizadaIntegrationEvent"/> -- ver <c>remarks</c> de
/// <see cref="WorkflowInstanciaIniciadaIntegrationEventConsumer"/> para el detalle del mecanismo Inbox
/// compartido.
/// </summary>
internal sealed class WorkflowInstanciaFinalizadaIntegrationEventConsumer(
    IRepository<ReporteWorkflowInstancia, Guid> repository,
    ILogger<WorkflowInstanciaFinalizadaIntegrationEventConsumer> logger)
    : IEventConsumer<WorkflowInstanciaFinalizadaIntegrationEvent>
{
    public async Task ConsumeAsync(
        WorkflowInstanciaFinalizadaIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var reporte = await repository.GetByIdAsync(integrationEvent.WorkflowInstanceId, cancellationToken);
        if (reporte is null)
        {
            // Caso de borde documentado en ReporteWorkflowInstancia.remarks: la finalización llegó antes
            // que el inicio de la misma instancia (tópicos distintos, sin garantía de orden entre ellos,
            // regla dura 22). A diferencia de TaskInboxItem.CrearYaResuelta, acá no hace falta ningún
            // marcador "desconocido": WorkflowDefinitionId viaja en AMBOS eventos.
            var nuevo = ReporteWorkflowInstancia.CrearYaFinalizada(
                integrationEvent.WorkflowInstanceId, integrationEvent.WorkflowDefinitionId,
                integrationEvent.EstadoFinalId, integrationEvent.OccurredOnUtc);
            await repository.AddAsync(nuevo, cancellationToken);
            return;
        }

        reporte.AplicarFinalizacion(integrationEvent.EstadoFinalId, integrationEvent.OccurredOnUtc);
        AdvertirSiDuracionQuedoSinCalcular(reporte, logger);
        repository.Update(reporte);
    }

    /// <summary>Hallazgo Medio de auditoría de arquitectura (2026-09-09): una duración negativa/no
    /// calculable (ver <c>ReporteWorkflowInstancia.RecalcularDuracion</c>) se descartaba en silencio --
    /// puede ocultar un problema real de reloj/orden en Workflow (el productor) sin que ningún operador se
    /// entere. Un log de advertencia (no una excepción: el reporte de todas formas queda persistido, solo
    /// sin duración) deja rastro observable para investigar el caso.</summary>
    internal static void AdvertirSiDuracionQuedoSinCalcular(ReporteWorkflowInstancia reporte, ILogger logger)
    {
        if (reporte is { IniciadaAtUtc: not null, FinalizadaAtUtc: not null, DuracionSegundos: null })
        {
            logger.LogWarning(
                "ReporteWorkflowInstancia {WorkflowInstanceId}: no se pudo calcular una duración válida " +
                "(IniciadaAtUtc={IniciadaAtUtc}, FinalizadaAtUtc={FinalizadaAtUtc}) -- posible reloj " +
                "desincronizado o dato inconsistente en el productor (Workflow).",
                reporte.Id, reporte.IniciadaAtUtc, reporte.FinalizadaAtUtc);
        }
    }
}
