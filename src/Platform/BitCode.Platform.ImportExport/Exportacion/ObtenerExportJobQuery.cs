using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>Progreso de UN <see cref="ExportJob"/> -- simétrico a <c>Importacion.ObtenerImportJobQuery</c>,
/// mismo criterio de autorización (solo RBAC, sin ownership adicional).</summary>
internal sealed record ObtenerExportJobQuery(Guid Id) : IQuery<ExportJobResponse>;

internal sealed class ObtenerExportJobQueryHandler(IReadRepository<ExportJob, Guid> repository)
    : IRequestHandler<ObtenerExportJobQuery, Result<ExportJobResponse>>
{
    public async Task<Result<ExportJobResponse>> Handle(ObtenerExportJobQuery request, CancellationToken cancellationToken)
    {
        var exportJob = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (exportJob is null)
        {
            return Result.Failure<ExportJobResponse>(Error.NotFound(
                "ImportExport.Exportaciones.NoEncontrada", $"No existe el trabajo de exportación {request.Id}."));
        }

        return Map(exportJob);
    }

    internal static ExportJobResponse Map(ExportJob j) => new(
        j.Id, j.TipoExportacion, j.Estado, j.FilasTotales, j.FilasExportadas, j.FilasConError, j.ErrorMensaje, j.FinalizadoAtUtc);
}
