using BitCode.Framework.Platform.FeatureManagement.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

/// <summary>Simétrico de <see cref="ActivarFeatureFlagCommand"/> -- mismo permiso distinto
/// (<see cref="FeatureManagementPermissions.FlagsDesactivar"/>) y misma regla ABAC de alcance por
/// <c>featureFlagId</c>. Apagar un flag en producción es tan sensible como encenderlo (puede desactivar
/// una capacidad de la que dependen usuarios reales).</summary>
internal sealed record DesactivarFeatureFlagCommand(Guid FeatureFlagId) : ICommand, IIdempotentCommand;

internal sealed class DesactivarFeatureFlagCommandValidator : AbstractValidator<DesactivarFeatureFlagCommand>
{
    public DesactivarFeatureFlagCommandValidator() => RuleFor(c => c.FeatureFlagId).NotEmpty();
}

internal sealed class DesactivarFeatureFlagCommandHandler(
    IRepository<FeatureFlag, Guid> repository,
    IAuthorizationPolicyEvaluator authorizationPolicyEvaluator,
    IAuditWriter auditWriter,
    IFeatureManagementActorContext actorContext)
    : IRequestHandler<DesactivarFeatureFlagCommand, Result>
{
    public const string ResourceType = "featuremanagement.flags";
    public const string Action = "desactivar";
    public const string FeatureFlagIdAttribute = "featureFlagId";

    public async Task<Result> Handle(DesactivarFeatureFlagCommand request, CancellationToken cancellationToken)
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

        var desactivarResult = flag.Desactivar();
        if (desactivarResult.IsFailure)
        {
            return desactivarResult;
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
            action: "featuremanagement.flags.desactivar",
            resource: new AuditResource(ResourceType, flag.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["nombre"] = flag.Nombre });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
