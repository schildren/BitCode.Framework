using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

/// <summary>
/// Listado paginado del read-model de referencia (Fase 6, módulo 11: "read models" del Plan Maestro), con
/// los mismos filtros que <see cref="ExportarReporteWorkflowInstanciasQuery"/> -- un cliente real primero
/// pagina/explora acá y después exporta el resultado completo con los mismos filtros.
/// </summary>
internal sealed record ListarReporteWorkflowInstanciasQuery(
    Guid? WorkflowDefinitionId, ReporteWorkflowInstanciaEstado? Estado, DateTime? DesdeUtc, DateTime? HastaUtc,
    int Page, int PageSize)
    : IQuery<PagedResult<ReporteWorkflowInstanciaResponse>>;

internal sealed class ListarReporteWorkflowInstanciasQueryHandler(
    IReadRepository<ReporteWorkflowInstancia, Guid> repository)
    : IRequestHandler<ListarReporteWorkflowInstanciasQuery, Result<PagedResult<ReporteWorkflowInstanciaResponse>>>
{
    public async Task<Result<PagedResult<ReporteWorkflowInstanciaResponse>>> Handle(
        ListarReporteWorkflowInstanciasQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<ReporteWorkflowInstanciaResponse>>(pageRequestResult.Error);
        }

        var specification = new ReporteWorkflowInstanciaFiltroSpecification(
            request.WorkflowDefinitionId, request.Estado, request.DesdeUtc, request.HastaUtc);

        return await repository.ListPagedAsync(
            specification,
            r => new ReporteWorkflowInstanciaResponse(
                r.Id, r.WorkflowDefinitionId, r.Estado, r.IniciadaAtUtc, r.FinalizadaAtUtc, r.DuracionSegundos, r.EstadoFinalId),
            pageRequestResult.Value,
            cancellationToken);
    }
}
