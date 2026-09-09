using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

internal sealed record ListarExportJobsQuery(string? TipoExportacion, ExportJobEstado? Estado, int Page, int PageSize)
    : IQuery<PagedResult<ExportJobResponse>>;

internal sealed class ListarExportJobsQueryHandler(IReadRepository<ExportJob, Guid> repository)
    : IRequestHandler<ListarExportJobsQuery, Result<PagedResult<ExportJobResponse>>>
{
    public async Task<Result<PagedResult<ExportJobResponse>>> Handle(ListarExportJobsQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<ExportJobResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosExportJobsSpecification(request.TipoExportacion, request.Estado),
            j => new ExportJobResponse(
                j.Id, j.TipoExportacion, j.Estado, j.FilasTotales, j.FilasExportadas, j.FilasConError, j.ErrorMensaje, j.FinalizadoAtUtc),
            pageRequestResult.Value,
            cancellationToken);
    }
}
