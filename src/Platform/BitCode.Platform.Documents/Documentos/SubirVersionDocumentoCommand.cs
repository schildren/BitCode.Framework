using System.Security.Cryptography;
using BitCode.Framework.Platform.Documents.Actors;
using BitCode.Framework.Platform.Documents.Almacenamiento;
using BitCode.Framework.Platform.Documents.Antivirus;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Sube una versión NUEVA sobre un documento existente (Épica de Documents: "Versionado" -- nunca borra ni
/// reemplaza el contenido de la versión anterior). Mismo criterio de <see cref="CrearDocumentoCommand"/>
/// para recibir <c>byte[]</c> en vez de un <c>Stream</c> vivo (compatibilidad con
/// <see cref="IIdempotentCommand"/>). Operación sensible de referencia del módulo (checklist Fase 6,
/// requisito común 7 "RBAC y ABAC"): el endpoint exige <see cref="DocumentsPermissions.DocumentosSubirVersion"/>
/// y el handler además evalúa explícitamente <see cref="IAuthorizationPolicyEvaluator"/> (F2-08) con la
/// regla ABAC incorporada del framework (<see cref="AttributeScopeAbacRule"/>) sobre el atributo
/// <c>documentoId</c> -- un consumidor real restringe qué documentos puede versionar un actor cuyo rol
/// solo administra un subconjunto de documentos, configurando <c>AbacOptions.ScopeRules</c> con
/// <c>ResourceType = "documents.documentos"</c>, <c>ResourceAttributeKey = "documentoId"</c> y el
/// <c>ClaimType</c> que transporte el alcance del actor -- ver <c>docs/guia-documents.md</c>, sección
/// "RBAC + ABAC por documento". Implementa <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record SubirVersionDocumentoCommand(
    Guid DocumentoId,
    string NombreArchivo,
    string ContentType,
    byte[] Contenido) : ICommand<Guid>, IIdempotentCommand;

internal sealed class SubirVersionDocumentoCommandValidator : AbstractValidator<SubirVersionDocumentoCommand>
{
    public SubirVersionDocumentoCommandValidator()
    {
        RuleFor(c => c.DocumentoId).NotEmpty();
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(256);
        RuleFor(c => c.ContentType).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Contenido).NotEmpty();
    }
}

internal sealed class SubirVersionDocumentoCommandHandler(
    IRepository<Documento, Guid> documentoRepository,
    IRepository<DocumentoVersion, Guid> versionRepository,
    IDocumentBlobStore blobStore,
    IAntivirusScanner antivirusScanner,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IAuditWriter auditWriter,
    IDocumentsActorContext actorContext)
    : IRequestHandler<SubirVersionDocumentoCommand, Result<Guid>>
{
    public const string ResourceType = "documents.documentos";
    public const string Action = "subirversion";
    public const string DocumentoIdAttribute = "documentoId";

    public async Task<Result<Guid>> Handle(SubirVersionDocumentoCommand request, CancellationToken cancellationToken)
    {
        var documento = await documentoRepository.GetByIdAsync(request.DocumentoId, cancellationToken);
        if (documento is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Documents.Documentos.NoEncontrado", $"No existe el documento {request.DocumentoId}."));
        }

        var principal = actorContext.GetCurrentPrincipal();
        if (principal is null)
        {
            // Fail-closed (mismo criterio que ActivarFeatureFlagCommandHandler/DesactivarEmpresaCommandHandler):
            // sin un ClaimsPrincipal resuelto no hay forma de evaluar la regla ABAC de alcance, así que la
            // operación se deniega en vez de omitir el control.
            return Result.Failure<Guid>(new Error(
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
            await WriteAuditEntryAsync(documento, null, AuditOutcome.Denied, decision.Reason, cancellationToken);
            return Result.Failure<Guid>(new Error(
                "Documents.Documentos.NoAutorizado", decision.Reason, ErrorType.Forbidden));
        }

        var hash = Convert.ToHexString(SHA256.HashData(request.Contenido));
        var scanResult = await antivirusScanner.ScanAsync(request.Contenido, cancellationToken);

        var numero = documento.VersionActualNumero + 1;
        var versionId = Guid.NewGuid();
        var blobKey = BlobKeyFactory.Crear(documento.Id, versionId);

        await blobStore.UploadAsync(blobKey, new MemoryStream(request.Contenido, writable: false), cancellationToken);

        var version = new DocumentoVersion(
            versionId, documento.Id, numero, request.NombreArchivo, request.ContentType,
            request.Contenido.LongLength, hash, blobKey);
        var escaneoResult = version.MarcarEscaneada(scanResult);
        if (escaneoResult.IsFailure)
        {
            return Result.Failure<Guid>(escaneoResult.Error);
        }

        var registrarResult = documento.RegistrarNuevaVersion(version);
        if (registrarResult.IsFailure)
        {
            return Result.Failure<Guid>(registrarResult.Error);
        }

        await versionRepository.AddAsync(version, cancellationToken);
        documentoRepository.Update(documento);

        await WriteAuditEntryAsync(documento, version, AuditOutcome.Success, null, cancellationToken);

        return version.Id;
    }

    private async Task WriteAuditEntryAsync(
        Documento documento, DocumentoVersion? version, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: documento.TenantId == Guid.Empty ? null : documento.TenantId,
            action: "documents.documentos.subirversion",
            resource: new AuditResource(ResourceType, documento.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: version is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["numero"] = version.Numero.ToString(), ["hashSha256"] = version.HashSha256 });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
