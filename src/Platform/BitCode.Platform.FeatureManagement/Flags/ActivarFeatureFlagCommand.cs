using BitCode.Framework.Platform.FeatureManagement.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

/// <summary>
/// Enciende un flag -- la operación sensible de referencia del módulo (checklist Fase 6, requisito
/// común 7 "RBAC y ABAC"): el endpoint exige el permiso RBAC
/// <see cref="FeatureManagementPermissions.FlagsActivar"/> (distinto y más restrictivo que
/// <see cref="FeatureManagementPermissions.FlagsCrear"/>) vía <c>RequireAuthorization</c>, y el handler
/// además evalúa explícitamente <see cref="IAuthorizationPolicyEvaluator"/> (F2-08) con la regla ABAC
/// incorporada del framework (<see cref="AttributeScopeAbacRule"/>) sobre el atributo
/// <c>featureFlagId</c> -- un consumidor real restringe qué flags puede activar un actor cuyo rol solo
/// administra un subconjunto de flags, configurando <c>AbacOptions.ScopeRules</c> con
/// <c>ResourceType = "featuremanagement.flags"</c>, <c>ResourceAttributeKey = "featureFlagId"</c> y el
/// <c>ClaimType</c> que transporte el alcance del actor -- ver <c>docs/guia-feature-management.md</c>,
/// sección "RBAC + ABAC al activar/desactivar un flag". Requiere que el proyecto consumidor haya
/// registrado <c>AddSharedAbacAuthorization</c> (F2-08). Implementa <see cref="IIdempotentCommand"/>
/// (F1-22).
/// </summary>
internal sealed record ActivarFeatureFlagCommand(Guid FeatureFlagId) : ICommand, IIdempotentCommand;

internal sealed class ActivarFeatureFlagCommandValidator : AbstractValidator<ActivarFeatureFlagCommand>
{
    public ActivarFeatureFlagCommandValidator() => RuleFor(c => c.FeatureFlagId).NotEmpty();
}

internal sealed class ActivarFeatureFlagCommandHandler(
    IRepository<FeatureFlag, Guid> repository,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IAuditWriter auditWriter,
    IFeatureManagementActorContext actorContext)
    : IRequestHandler<ActivarFeatureFlagCommand, Result>
{
    public const string ResourceType = "featuremanagement.flags";
    public const string Action = "activar";
    public const string FeatureFlagIdAttribute = "featureFlagId";

    public async Task<Result> Handle(ActivarFeatureFlagCommand request, CancellationToken cancellationToken)
    {
        var flag = await repository.GetByIdAsync(request.FeatureFlagId, cancellationToken);
        if (flag is null)
        {
            return Result.Failure(Error.NotFound(
                "FeatureManagement.Flags.NoEncontrado", $"No existe el flag {request.FeatureFlagId}."));
        }

        var principal = actorContext.GetCurrentPrincipal();
        if (principal is null)
        {
            // Fail-closed (mismo criterio que PublicarCatalogoVersionCommandHandler/
            // DesactivarEmpresaCommandHandler): sin un ClaimsPrincipal resuelto no hay forma de evaluar
            // la regla ABAC de alcance, así que la operación se deniega en vez de omitir el control.
            return Result.Failure(new Error(
                "FeatureManagement.Flags.ActorNoResuelto",
                "No se pudo resolver el actor autenticado para evaluar la autorización.",
                ErrorType.Forbidden));
        }

        var resource = new AbacResource(
            ResourceType,
            new Dictionary<string, object?> { [FeatureFlagIdAttribute] = flag.Id.ToString() });

        var decision = await authorizationPolicyEvaluator.EvaluateAsync(
            principal, resource, Action, cancellationToken: cancellationToken);

        if (!decision.Allowed)
        {
            await WriteAuditEntryAsync(flag, AuditOutcome.Denied, decision.Reason, cancellationToken);
            return Result.Failure(new Error(
                "FeatureManagement.Flags.NoAutorizado", decision.Reason, ErrorType.Forbidden));
        }

        var activarResult = flag.Activar();
        if (activarResult.IsFailure)
        {
            return activarResult;
        }

        repository.Update(flag);

        await WriteAuditEntryAsync(flag, AuditOutcome.Success, null, cancellationToken);

        return Result.Success();
    }

    private async Task WriteAuditEntryAsync(FeatureFlag flag, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: flag.TenantId == Guid.Empty ? null : flag.TenantId,
            action: "featuremanagement.flags.activar",
            resource: new AuditResource(ResourceType, flag.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["nombre"] = flag.Nombre });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
