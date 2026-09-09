using System.Text.Json;
using BitCode.Framework.Platform.ImportExport.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.ImportExport.Exportacion;

/// <summary>
/// Alta de un <see cref="ExportJob"/> (Fase 6, módulo 10) -- solo crea la fila en
/// <see cref="ExportJobEstado.Pendiente"/>, sin archivo todavía; es
/// <c>Procesamiento.ExportBatchProcessorJob</c> quien crea y completa el archivo de resultado en ciclos
/// posteriores. Implementa <see cref="IIdempotentCommand"/> (F1-22): un POST repetido con la misma
/// Idempotency-Key y el mismo filtro no encola una segunda exportación duplicada.
/// </summary>
internal sealed record IniciarExportacionCommand(string TipoExportacion, string? FiltroJson) : ICommand<Guid>, IIdempotentCommand;

internal sealed class IniciarExportacionCommandValidator : AbstractValidator<IniciarExportacionCommand>
{
    public IniciarExportacionCommandValidator()
    {
        RuleFor(c => c.TipoExportacion).NotEmpty().MaximumLength(128);
        RuleFor(c => c.FiltroJson).Must(EsJsonValidoOVacio)
            .WithMessage("FiltroJson debe ser un documento JSON válido cuando se provee.");
    }

    private static bool EsJsonValidoOVacio(string? filtro)
    {
        if (string.IsNullOrWhiteSpace(filtro))
        {
            return true;
        }

        try
        {
            using var _ = JsonDocument.Parse(filtro);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed class IniciarExportacionCommandHandler(
    IEnumerable<IExportDataSource> dataSources,
    IRepository<ExportJob, Guid> exportJobRepository,
    IAuditWriter auditWriter,
    IImportExportActorContext actorContext)
    : IRequestHandler<IniciarExportacionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(IniciarExportacionCommand request, CancellationToken cancellationToken)
    {
        var fuenteExiste = dataSources.Any(d => d.TipoExportacion == request.TipoExportacion);
        if (!fuenteExiste)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "ImportExport.Exportaciones.TipoNoRegistrado",
                $"No hay una fuente de exportación registrada para el tipo '{request.TipoExportacion}'."));
        }

        var exportJobId = Guid.NewGuid();
        var blobKey = $"exportaciones/{exportJobId}/resultado.csv";

        var exportJob = new ExportJob(exportJobId, request.TipoExportacion, request.FiltroJson, blobKey, actorContext.GetCurrentUserId());
        await exportJobRepository.AddAsync(exportJob, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "importexport.exportaciones.iniciar",
            resource: new AuditResource("importexport.exportaciones", exportJob.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["tipoExportacion"] = request.TipoExportacion,
                ["filtroJson"] = request.FiltroJson,
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return exportJob.Id;
    }
}
