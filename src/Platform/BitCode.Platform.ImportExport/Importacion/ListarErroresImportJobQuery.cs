using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

internal sealed record ListarErroresImportJobQuery(Guid ImportJobId) : IQuery<IReadOnlyList<ImportJobErrorResponse>>;

internal sealed class ListarErroresImportJobQueryHandler(
    IReadRepository<ImportJob, Guid> importJobRepository, IReadRepository<ImportJobError, Guid> errorRepository)
    : IRequestHandler<ListarErroresImportJobQuery, Result<IReadOnlyList<ImportJobErrorResponse>>>
{
    public async Task<Result<IReadOnlyList<ImportJobErrorResponse>>> Handle(
        ListarErroresImportJobQuery request, CancellationToken cancellationToken)
    {
        var importJob = await importJobRepository.GetByIdAsync(request.ImportJobId, cancellationToken);
        if (importJob is null)
        {
            return Result.Failure<IReadOnlyList<ImportJobErrorResponse>>(Error.NotFound(
                "ImportExport.Importaciones.NoEncontrada", $"No existe el trabajo de importación {request.ImportJobId}."));
        }

        var errores = await errorRepository.ListAsync(
            new ErroresDeImportJobSpecification(request.ImportJobId),
            e => new ImportJobErrorResponse(e.Id, e.NumeroFila, e.MensajeError, e.ContenidoFilaCrudo),
            cancellationToken);

        return Result.Success(errores);
    }
}
