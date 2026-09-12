using BitCode.Framework.Platform.ImportExport.Almacenamiento;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>Descarga el archivo de resultado de un <see cref="ExportJob"/> ya finalizado -- devuelve el
/// contenido como archivo adjunto (mismo criterio que <c>DescargarDocumentoVersionQuery</c>, Fase 6 módulo
/// 5): nunca inline, para que el cliente nunca intente renderizar el CSV como si fuera HTML.</summary>
internal sealed record DescargarResultadoExportJobQuery(Guid Id) : IQuery<ExportJobDescargaResponse>;

internal sealed class DescargarResultadoExportJobQueryHandler(
    IReadRepository<ExportJob, Guid> repository, IImportExportFileStore fileStore)
    : IRequestHandler<DescargarResultadoExportJobQuery, Result<ExportJobDescargaResponse>>
{
    public async Task<Result<ExportJobDescargaResponse>> Handle(
        DescargarResultadoExportJobQuery request, CancellationToken cancellationToken)
    {
        var exportJob = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (exportJob is null)
        {
            return Result.Failure<ExportJobDescargaResponse>(Error.NotFound(
                "ImportExport.Exportaciones.NoEncontrada", $"No existe el trabajo de exportación {request.Id}."));
        }

        if (exportJob.Estado is not (ExportJobEstado.Completado or ExportJobEstado.CompletadoConErrores))
        {
            return Result.Failure<ExportJobDescargaResponse>(Error.Conflict(
                "ImportExport.Exportaciones.AunNoFinalizada",
                $"El trabajo de exportación {request.Id} todavía no finalizó (estado actual: {exportJob.Estado})."));
        }

        try
        {
            await using var stream = await fileStore.OpenReadAsync(exportJob.ArchivoResultadoBlobKey, cancellationToken);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);

            return new ExportJobDescargaResponse(memoryStream.ToArray(), "text/csv", $"{exportJob.TipoExportacion}-{exportJob.Id}.csv");
        }
        catch (FileNotFoundException)
        {
            // Metadata presente, archivo físico ausente -- mismo criterio de recuperación honesta que
            // DescargarDocumentoVersionQueryHandler (Fase 6, módulo 5): nunca un 500 no controlado.
            return Result.Failure<ExportJobDescargaResponse>(Error.NotFound(
                "ImportExport.Exportaciones.ArchivoResultadoAusente",
                $"El archivo de resultado del trabajo de exportación {request.Id} ya no está disponible."));
        }
    }
}
