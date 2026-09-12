using BitCode.Framework.Platform.Documents.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Dispone (da de baja lógica) un documento cuya retención ya venció (Épica de Documents: "Retención y
/// disposición"). Deliberadamente NO hay un job automático que dispare esto (ver
/// <c>docs/guia-documents.md</c>, sección "Pendientes", mismo criterio de simplificación que
/// <c>Retention-Cleanup.sql</c> de F5-07): este primer corte entrega el CÁLCULO de la fecha
/// (<see cref="Documento.DisponibleParaDisposicionDesde"/>) y el GUARDRAIL que impide disponer antes de
/// tiempo -- un operador (o un job externo futuro) dispara la disposición real llamando este endpoint.
/// Misma sensibilidad y mismo mecanismo RBAC + ABAC que <see cref="SubirVersionDocumentoCommand"/>/
/// <see cref="DescargarDocumentoVersionQuery"/> (permiso <see cref="DocumentsPermissions.DocumentosDisponer"/>
/// + regla ABAC de alcance por <c>documentoId</c>). Implementa <see cref="IIdempotentCommand"/> (F1-22).
/// </summary>
internal sealed record DisponerDocumentoCommand(Guid DocumentoId) : ICommand, IIdempotentCommand;

internal sealed class DisponerDocumentoCommandValidator : AbstractValidator<DisponerDocumentoCommand>
{
    public DisponerDocumentoCommandValidator() => RuleFor(c => c.DocumentoId).NotEmpty();
}

internal sealed class DisponerDocumentoCommandHandler(
    IRepository<Documento, Guid> repository,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IAuditWriter auditWriter,
    IDocumentsActorContext actorContext)
    : IRequestHandler<DisponerDocumentoCommand, Result>
{
    public const string ResourceType = "documents.documentos";
    public const string Action = "disponer";
    public const string DocumentoIdAttribute = "documentoId";

    public async Task<Result> Handle(DisponerDocumentoCommand request, CancellationToken cancellationToken)
    {
        var documento = await repository.GetByIdAsync(request.DocumentoId, cancellationToken);
        if (documento is null)
        {
            return Result.Failure(Error.NotFound(
                "Documents.Documentos.NoEncontrado", $"No existe el documento {request.DocumentoId}."));
        }

        var principal = actorContext.GetCurrentPrincipal();
        if (principal is null)
        {
            return Result.Failure(new Error(
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
            await WriteAuditEntryAsync(documento, AuditOutcome.Denied, decision.Reason, cancellationToken);
            return Result.Failure(new Error(
                "Documents.Documentos.NoAutorizado", decision.Reason, ErrorType.Forbidden));
        }

        var puedeDisponerseResult = documento.PuedeDisponerse(DateTime.UtcNow);
        if (puedeDisponerseResult.IsFailure)
        {
            await WriteAuditEntryAsync(documento, AuditOutcome.Denied, puedeDisponerseResult.Error.Description, cancellationToken);
            return puedeDisponerseResult;
        }

        repository.Remove(documento);

        await WriteAuditEntryAsync(documento, AuditOutcome.Success, null, cancellationToken);

        return Result.Success();
    }

    private async Task WriteAuditEntryAsync(Documento documento, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: documento.TenantId == Guid.Empty ? null : documento.TenantId,
            action: "documents.documentos.disponer",
            resource: new AuditResource(ResourceType, documento.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["titulo"] = documento.Titulo });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
