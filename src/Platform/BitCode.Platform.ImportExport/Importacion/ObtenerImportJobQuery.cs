using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

/// <summary>
/// Progreso de UN <see cref="ImportJob"/> (Fase 6, módulo 10: "progreso" del Plan Maestro) -- consultable
/// en cualquier momento mientras el job avanza en background, para que un cliente real muestre una barra
/// de progreso (<see cref="ImportJobResponse.FilasProcesadas"/> / <see cref="ImportJobResponse.FilasTotales"/>).
/// SIN verificación de ownership adicional más allá del permiso RBAC genérico
/// <see cref="ImportExportPermissions.ImportacionesVer"/> -- mismo criterio que
/// <c>ObtenerSolicitudQuery</c> (Fase 6, módulo 9): un <see cref="ImportJob"/> no es un dato personal de
/// ningún usuario final, es un dato operacional de una operación masiva sobre datos de negocio.
/// <see cref="ImportJob.IniciadoPorUserId"/> existe para trazabilidad/auditoría, no para restringir quién
/// puede leer la fila.
/// </summary>
internal sealed record ObtenerImportJobQuery(Guid Id) : IQuery<ImportJobResponse>;

internal sealed class ObtenerImportJobQueryHandler(IReadRepository<ImportJob, Guid> repository)
    : IRequestHandler<ObtenerImportJobQuery, Result<ImportJobResponse>>
{
    public async Task<Result<ImportJobResponse>> Handle(ObtenerImportJobQuery request, CancellationToken cancellationToken)
    {
        var importJob = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (importJob is null)
        {
            return Result.Failure<ImportJobResponse>(Error.NotFound(
                "ImportExport.Importaciones.NoEncontrada", $"No existe el trabajo de importación {request.Id}."));
        }

        return Map(importJob);
    }

    internal static ImportJobResponse Map(ImportJob j) => new(
        j.Id, j.TipoImportacion, j.NombreArchivoOriginal, j.Estado, j.FilasTotales, j.FilasProcesadas, j.FilasConError,
        j.ErrorMensaje, j.FinalizadoAtUtc);
}
