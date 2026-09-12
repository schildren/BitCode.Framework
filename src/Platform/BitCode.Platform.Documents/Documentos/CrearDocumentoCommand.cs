using System.Security.Cryptography;
using BitCode.Framework.Platform.Documents.Actors;
using BitCode.Framework.Platform.Documents.Almacenamiento;
using BitCode.Framework.Platform.Documents.Antivirus;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Alta de un documento con su primera versión (Épica de Documents: "Metadata y clasificación",
/// "Versionado", "Hash de contenido", "Carga ... segura", "Escaneo antivirus", "Retención y disposición").
/// Recibe el contenido como <c>byte[]</c> (no un <c>Stream</c>/<c>IFormFile</c> vivo) DELIBERADAMENTE: un
/// <see cref="IIdempotentCommand"/> se serializa a JSON para calcular el hash de deduplicación
/// (<c>IdempotencyBehavior.ComputeRequestHash</c>) -- un <c>Stream</c> no serializa de forma útil ni
/// determinística, un <c>byte[]</c> sí (Base64). El endpoint (<c>DocumentsEndpointRouteBuilderExtensions</c>)
/// materializa el <c>IFormFile</c> recibido a <c>byte[]</c> antes de construir este comando. Implementa
/// <see cref="IIdempotentCommand"/> (F1-22): un POST repetido con la misma Idempotency-Key y el mismo
/// archivo no crea un segundo documento.
/// </summary>
internal sealed record CrearDocumentoCommand(
    string Titulo,
    string? Descripcion,
    string Clasificacion,
    int RetencionDias,
    string NombreArchivo,
    string ContentType,
    byte[] Contenido) : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearDocumentoCommandValidator : AbstractValidator<CrearDocumentoCommand>
{
    public CrearDocumentoCommandValidator()
    {
        RuleFor(c => c.Titulo).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Descripcion).MaximumLength(1000);
        RuleFor(c => c.Clasificacion).NotEmpty().MaximumLength(128);
        RuleFor(c => c.RetencionDias).GreaterThanOrEqualTo(0);
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(256);
        RuleFor(c => c.ContentType).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Contenido).NotEmpty();
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, docs/convenciones.md):
/// <c>TransactionBehavior</c> lo hace al final del pipeline. El contenido se escribe en
/// <see cref="IDocumentBlobStore"/> ANTES de agregar la metadata al repositorio -- si la escritura del
/// blob falla, el comando falla sin dejar ningún registro de <see cref="Documento"/>/<see cref="DocumentoVersion"/>
/// huérfano (metadata sin archivo). El orden inverso podría dejar exactamente ese estado inconsistente si
/// el blob store fallara después del commit de la transacción de base de datos -- ver
/// <c>docs/guia-documents.md</c>, sección "Consistencia metadata/blob".
/// </summary>
internal sealed class CrearDocumentoCommandHandler(
    IRepository<Documento, Guid> documentoRepository,
    IRepository<DocumentoVersion, Guid> versionRepository,
    IDocumentBlobStore blobStore,
    IAntivirusScanner antivirusScanner,
    IAuditWriter auditWriter,
    IDocumentsActorContext actorContext)
    : IRequestHandler<CrearDocumentoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearDocumentoCommand request, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(request.Contenido));
        var scanResult = await antivirusScanner.ScanAsync(request.Contenido, cancellationToken);

        var documento = new Documento(Guid.NewGuid(), request.Titulo, request.Descripcion, request.Clasificacion, request.RetencionDias);
        var versionId = Guid.NewGuid();
        var blobKey = BlobKeyFactory.Crear(documento.Id, versionId);

        await blobStore.UploadAsync(blobKey, new MemoryStream(request.Contenido, writable: false), cancellationToken);

        var version = new DocumentoVersion(
            versionId, documento.Id, numero: 1, request.NombreArchivo, request.ContentType,
            request.Contenido.LongLength, hash, blobKey);
        var escaneoResult = version.MarcarEscaneada(scanResult);
        if (escaneoResult.IsFailure)
        {
            return Result.Failure<Guid>(escaneoResult.Error);
        }

        var registrarResult = documento.RegistrarVersionInicial(version);
        if (registrarResult.IsFailure)
        {
            return Result.Failure<Guid>(registrarResult.Error);
        }

        await documentoRepository.AddAsync(documento, cancellationToken);
        await versionRepository.AddAsync(version, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "documents.documentos.crear",
            resource: new AuditResource("documents.documentos", documento.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["titulo"] = documento.Titulo,
                ["clasificacion"] = documento.Clasificacion,
                ["hashSha256"] = hash,
                ["estadoEscaneo"] = scanResult.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return documento.Id;
    }
}
