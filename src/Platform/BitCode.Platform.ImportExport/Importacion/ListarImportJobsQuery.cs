using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

internal sealed record ListarImportJobsQuery(string? TipoImportacion, ImportJobEstado? Estado, int Page, int PageSize)
    : IQuery<PagedResult<ImportJobResponse>>;

internal sealed class ListarImportJobsQueryHandler(IReadRepository<ImportJob, Guid> repository)
    : IRequestHandler<ListarImportJobsQuery, Result<PagedResult<ImportJobResponse>>>
{
    public async Task<Result<PagedResult<ImportJobResponse>>> Handle(ListarImportJobsQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<ImportJobResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosImportJobsSpecification(request.TipoImportacion, request.Estado),
            j => new ImportJobResponse(
                j.Id, j.TipoImportacion, j.NombreArchivoOriginal, j.Estado, j.FilasTotales, j.FilasProcesadas,
                j.FilasConError, j.ErrorMensaje, j.FinalizadoAtUtc),
            pageRequestResult.Value,
            cancellationToken);
    }
}
