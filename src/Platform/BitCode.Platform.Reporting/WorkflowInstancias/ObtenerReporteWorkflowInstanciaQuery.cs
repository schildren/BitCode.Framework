using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

internal sealed record ObtenerReporteWorkflowInstanciaQuery(Guid Id) : IQuery<ReporteWorkflowInstanciaResponse>;

internal sealed class ObtenerReporteWorkflowInstanciaQueryHandler(
    IReadRepository<ReporteWorkflowInstancia, Guid> repository)
    : IRequestHandler<ObtenerReporteWorkflowInstanciaQuery, Result<ReporteWorkflowInstanciaResponse>>
{
    public async Task<Result<ReporteWorkflowInstanciaResponse>> Handle(
        ObtenerReporteWorkflowInstanciaQuery request, CancellationToken cancellationToken)
    {
        var reporte = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (reporte is null)
        {
            return Result.Failure<ReporteWorkflowInstanciaResponse>(Error.NotFound(
                "Reporting.WorkflowInstancias.NoEncontrado", $"No existe el reporte de instancia {request.Id}."));
        }

        // Sin ownership: ver "RBAC y ABAC" en docs/guia-reporting.md -- read-model agregado/operacional,
        // no personal (mismo criterio honesto que Integration Hub, Fase 6 módulo 9).
        return Result.Success(new ReporteWorkflowInstanciaResponse(
            reporte.Id, reporte.WorkflowDefinitionId, reporte.Estado, reporte.IniciadaAtUtc,
            reporte.FinalizadaAtUtc, reporte.DuracionSegundos, reporte.EstadoFinalId));
    }
}
