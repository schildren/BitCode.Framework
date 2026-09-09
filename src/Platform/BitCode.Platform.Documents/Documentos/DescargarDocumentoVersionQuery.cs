using BitCode.Framework.Platform.Documents.Actors;
using BitCode.Framework.Platform.Documents.Almacenamiento;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>Contenido descargado, listo para que el endpoint lo devuelva como <c>Results.Bytes</c>/
/// <c>Results.File</c>.</summary>
internal sealed record DescargaDocumentoResponse(byte[] Contenido, string NombreArchivo, string ContentType, string HashSha256);

/// <summary>
/// Descarga el contenido de una versión de un documento -- <paramref name="Numero"/> nulo descarga la
/// versión VIGENTE (<see cref="Documento.VersionActualId"/>). Operación sensible de referencia del módulo
/// (checklist Fase 6, requisitos comunes 7 "RBAC y ABAC" y 8 "Auditoría de operaciones críticas": "sobre
/// todo la descarga de un documento sensible es un evento auditable"). Es deliberadamente un <see cref="IQuery{TResponse}"/>
/// (no modifica ninguna entidad de <see cref="DocumentsDbContext"/> -- regla dura 2, docs/convenciones.md)
/// aunque también evalúa ABAC y escribe una entrada de auditoría: ninguna de las dos operaciones pasa por
/// <c>IUnitOfWork</c>/<c>TransactionBehavior</c> (la auditoría usa <see cref="IAuditWriter"/>, un sumidero
/// independiente -- F2-15 -- no la base de datos de este módulo), así que no hay ningún cambio de datos
/// de negocio sin la protección transaccional que esa regla busca evitar.
/// </summary>
internal sealed record DescargarDocumentoVersionQuery(Guid DocumentoId, int? Numero) : IQuery<DescargaDocumentoResponse>;

internal sealed class DescargarDocumentoVersionQueryHandler(
    IReadRepository<Documento, Guid> documentoRepository,
    IReadRepository<DocumentoVersion, Guid> versionRepository,
    IDocumentBlobStore blobStore,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IAuditWriter auditWriter,
    IDocumentsActorContext actorContext)
    : IRequestHandler<DescargarDocumentoVersionQuery, Result<DescargaDocumentoResponse>>
{
    public const string ResourceType = "documents.documentos";
    public const string Action = "descargar";
    public const string DocumentoIdAttribute = "documentoId";

    public async Task<Result<DescargaDocumentoResponse>> Handle(
        DescargarDocumentoVersionQuery request, CancellationToken cancellationToken)
    {
        var documento = await documentoRepository.GetByIdAsync(request.DocumentoId, cancellationToken);
        if (documento is null)
        {
            return Result.Failure<DescargaDocumentoResponse>(Error.NotFound(
                "Documents.Documentos.NoEncontrado", $"No existe el documento {request.DocumentoId}."));
        }

        var version = request.Numero is null
            ? await versionRepository.GetByIdAsync(documento.VersionActualId, cancellationToken)
            : (await versionRepository.ListAsync(
                new VersionDeDocumentoPorNumeroSpecification(documento.Id, request.Numero.Value), cancellationToken))
                .SingleOrDefault();

        if (version is null)
        {
            return Result.Failure<DescargaDocumentoResponse>(Error.NotFound(
                "Documents.Versiones.NoEncontrada",
                $"No existe la versión {request.Numero?.ToString() ?? "vigente"} del documento {request.DocumentoId}."));
        }

        var principal = actorContext.GetCurrentPrincipal();
        if (principal is null)
        {
            // Fail-closed (mismo criterio que SubirVersionDocumentoCommandHandler): sin un ClaimsPrincipal
            // resuelto no hay forma de evaluar la regla ABAC de alcance, así que la operación se deniega
            // en vez de omitir el control.
            return Result.Failure<DescargaDocumentoResponse>(new Error(
                "Documents.Documentos.ActorNoResuelto",
                "No se pudo resolver el actor autenticado para evaluar la autorización.",
                ErrorType.Forbidden));
        }

        var resource = new AbacResource(
            ResourceType, new Dictionary<string, object?> { [DocumentoIdAttribute] = documento.Id.ToString() });

        var decision = await authorizationPolicyEvaluator.EvaluateAsync(
            principal, resource, Action, cancellationToken: cancellationToken);

        if (!decision.Allowed)
        {
            await WriteAuditEntryAsync(documento, version, AuditOutcome.Denied, decision.Reason, cancellationToken);
            return Result.Failure<DescargaDocumentoResponse>(new Error(
                "Documents.Documentos.NoAutorizado", decision.Reason, ErrorType.Forbidden));
        }

        // Guardrail de negocio central del requisito "Escaneo antivirus" (Épica de Documents): una
        // versión Infectada, o todavía no escaneada, nunca se entrega -- ni siquiera a un actor
        // autorizado por RBAC/ABAC.
        if (!version.PuedeDescargarse())
        {
            var motivo = $"version-no-descargable:estado-escaneo={version.EstadoEscaneo}";
            await WriteAuditEntryAsync(documento, version, AuditOutcome.Denied, motivo, cancellationToken);
            return Result.Failure<DescargaDocumentoResponse>(Error.Conflict(
                "Documents.Versiones.NoDescargable",
                $"La versión {version.Numero} no puede descargarse (estado de escaneo: {version.EstadoEscaneo})."));
        }

        byte[] contenido;
        try
        {
            await using var stream = await blobStore.DownloadAsync(version.BlobKey, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            contenido = buffer.ToArray();
        }
        catch (FileNotFoundException ex)
        {
            // Criterio de aceptación de recuperación del Gate de salida de Fase 6: metadata presente,
            // archivo físico ausente (por ejemplo, borrado manual del almacenamiento subyacente, o una
            // escritura que nunca llegó a confirmarse del lado del blob store) nunca debe propagar una
            // excepción sin controlar hasta el cliente -- se degrada a un Result.Failure auditado.
            await WriteAuditEntryAsync(documento, version, AuditOutcome.Error, ex.Message, cancellationToken);
            return Result.Failure<DescargaDocumentoResponse>(Error.Failure(
                "Documents.Versiones.ContenidoNoDisponible",
                $"El contenido físico de la versión {version.Numero} no está disponible en el almacenamiento."));
        }

        await WriteAuditEntryAsync(documento, version, AuditOutcome.Success, null, cancellationToken);

        return new DescargaDocumentoResponse(contenido, version.NombreArchivo, version.ContentType, version.HashSha256);
    }

    private async Task WriteAuditEntryAsync(
        Documento documento, DocumentoVersion version, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: documento.TenantId == Guid.Empty ? null : documento.TenantId,
            action: "documents.documentos.descargar",
            resource: new AuditResource(ResourceType, documento.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["numero"] = version.Numero.ToString(), ["hashSha256"] = version.HashSha256 });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
