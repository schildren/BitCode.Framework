using BitCode.Framework.Platform.ImportExport.Actors;
using BitCode.Framework.Platform.ImportExport.Almacenamiento;
using BitCode.Framework.Platform.ImportExport.Csv;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Importacion;

/// <summary>
/// Alta de un <see cref="ImportJob"/> con su archivo ya guardado (Fase 6, módulo 10). Solo persiste el
/// archivo y la fila de <see cref="ImportJob"/> en <see cref="ImportJobEstado.Pendiente"/> -- nunca procesa
/// ninguna fila en este mismo request (ver el <c>remarks</c> de <see cref="ImportJob"/>); es
/// <c>Procesamiento.ImportBatchProcessorJob</c>, corriendo en ciclos posteriores, quien realmente valida y
/// aplica cada fila. Recibe el contenido como <c>byte[]</c> (no un <c>Stream</c>/<c>IFormFile</c> vivo)
/// DELIBERADAMENTE, mismo motivo que <c>CrearDocumentoCommand</c> (Fase 6, módulo 5): compatibilidad con
/// <see cref="IIdempotentCommand"/> (F1-22), que serializa el comando a JSON para calcular el hash de
/// deduplicación -- un POST repetido con la misma Idempotency-Key y el mismo archivo no encola una segunda
/// importación duplicada.
/// </summary>
internal sealed record IniciarImportacionCommand(string TipoImportacion, string NombreArchivo, byte[] Contenido)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class IniciarImportacionCommandValidator : AbstractValidator<IniciarImportacionCommand>
{
    public IniciarImportacionCommandValidator()
    {
        RuleFor(c => c.TipoImportacion).NotEmpty().MaximumLength(128);
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Contenido).NotEmpty()
            // Límite de tamaño DELIBERADAMENTE conservador (10 MB): este módulo lee el archivo completo en
            // memoria una vez, al alta, para contar sus filas de datos (ver Csv.CsvFileLineCounter) -- un
            // archivo de varios cientos de MB requeriría una estrategia de conteo en streaming, pendiente
            // explícito, ver docs/guia-import-export.md, sección "Límites de tamaño de archivo".
            .Must(c => c.Length <= 10 * 1024 * 1024)
            .WithMessage("El archivo no puede superar los 10 MB en este módulo de referencia.");
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, docs/convenciones.md):
/// <c>TransactionBehavior</c> lo hace al final del pipeline. El archivo se escribe en
/// <see cref="IImportExportFileStore"/> ANTES de agregar la metadata al repositorio -- mismo orden que
/// <c>CrearDocumentoCommand</c>, para nunca dejar un <see cref="ImportJob"/> huérfano apuntando a un blob
/// que no llegó a escribirse.
/// </summary>
internal sealed class IniciarImportacionCommandHandler(
    IEnumerable<IImportRowHandler> rowHandlers,
    IImportExportFileStore fileStore,
    IRepository<ImportJob, Guid> importJobRepository,
    IAuditWriter auditWriter,
    IImportExportActorContext actorContext)
    : IRequestHandler<IniciarImportacionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(IniciarImportacionCommand request, CancellationToken cancellationToken)
    {
        // Validado tempranamente (mismo criterio que EnviarSolicitudIntegracionCommand con su conector):
        // sin un handler registrado, el job de procesamiento fallaría siempre -- mejor rechazar el alta que
        // dejar un ImportJob condenado a Fallido desde el primer ciclo.
        var handlerExiste = rowHandlers.Any(h => h.TipoImportacion == request.TipoImportacion);
        if (!handlerExiste)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "ImportExport.Importaciones.TipoNoRegistrado",
                $"No hay un manejador de importación registrado para el tipo '{request.TipoImportacion}'."));
        }

        string contenidoTexto;
        try
        {
            // `System.Text.Encoding.UTF8` (la instancia estática) NO lanza ante bytes inválidos -- usa un
            // fallback de reemplazo silencioso (sustituye por U+FFFD) en vez de un fallback que lance. Con
            // esa instancia, el catch de abajo era código MUERTO: cualquier archivo, sin importar su
            // codificación real, "pasaba" como texto válido con caracteres corruptos en las filas
            // afectadas. Se construye acá una instancia PROPIA con `throwOnInvalidBytes: true` para que
            // esta validación haga lo que su nombre y su mensaje de error prometen -- hallazgo real
            // encontrado al agregar el test que la auditoría de arquitectura (2026-09-09) señaló como
            // faltante (la cobertura reveló que la validación nunca rechazaba nada).
            var utf8Estricto = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            contenidoTexto = utf8Estricto.GetString(request.Contenido);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Clasificado como error de VALIDACIÓN (permanente) -- un archivo que no es texto UTF-8 válido
            // nunca va a poder procesarse, sin importar cuántas veces se reintente.
            return Result.Failure<Guid>(Error.Validation(
                "ImportExport.Importaciones.ArchivoNoEsTextoValido", $"El archivo no es texto UTF-8 válido: {ex.Message}"));
        }

        var filasTotales = CsvFileLineCounter.ContarFilasDeDatos(contenidoTexto);

        var importJobId = Guid.NewGuid();
        var blobKey = $"importaciones/{importJobId}/original.csv";

        await fileStore.UploadAsync(blobKey, new MemoryStream(request.Contenido, writable: false), cancellationToken);

        var importJob = new ImportJob(
            importJobId, request.TipoImportacion, blobKey, request.NombreArchivo, filasTotales, actorContext.GetCurrentUserId());
        await importJobRepository.AddAsync(importJob, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "importexport.importaciones.iniciar",
            resource: new AuditResource("importexport.importaciones", importJob.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["tipoImportacion"] = request.TipoImportacion,
                ["nombreArchivo"] = request.NombreArchivo,
                ["filasTotales"] = filasTotales.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return importJob.Id;
    }
}
