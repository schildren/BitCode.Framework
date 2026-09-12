using BitCode.Framework.Platform.FeatureManagement.Actors;
using BitCode.Framework.Platform.FeatureManagement.Flags;
using BitCode.Framework.Platform.FeatureManagement.Segmentos;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Rollouts;

/// <summary>
/// Asocia un flag existente con un segmento existente (rollout hacia ese segmento). Implementa
/// <see cref="IIdempotentCommand"/> (F1-22).
///
/// Nota de concurrencia (mismo hallazgo documentado en Catalogs and Parameters, Fase 6 módulo 3, sección
/// "Pendientes" de <c>docs/guia-catalogs.md</c>, punto 4): la validación de "no duplicar la misma
/// asociación FeatureFlag-Segmento" (<c>AnyAsync</c>, check) ocurre antes de insertar (act), sin
/// <c>ITransactionalCommand</c> ni aislamiento serializable. A diferencia de ese hallazgo de Catalogs,
/// acá SÍ existe un guardrail de datos: el índice único <c>(FeatureFlagId, SegmentoId)</c> de
/// <see cref="FeatureManagementDbContext"/> evita que dos requests concurrentes terminen persistiendo la
/// misma asociación duplicada -- la carrera perdedora recibe una <c>DbUpdateException</c> no traducida a
/// <c>Result.Failure</c>/409 en este primer corte (el <c>ExceptionHandlerMiddleware</c> genérico la
/// convierte en un 500 en vez de un 409 amigable), documentado explícitamente como pendiente en
/// <c>docs/guia-feature-management.md</c> en vez de dejarlo sin mencionar.
/// </summary>
internal sealed record CrearRolloutCommand(Guid FeatureFlagId, Guid SegmentoId) : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearRolloutCommandValidator : AbstractValidator<CrearRolloutCommand>
{
    public CrearRolloutCommandValidator()
    {
        RuleFor(c => c.FeatureFlagId).NotEmpty();
        RuleFor(c => c.SegmentoId).NotEmpty();
    }
}

internal sealed class CrearRolloutCommandHandler(
    IReadRepository<FeatureFlag, Guid> flagRepository,
    IReadRepository<Segmento, Guid> segmentoRepository,
    IRepository<Rollout, Guid> rolloutRepository,
    IAuditWriter auditWriter,
    IFeatureManagementActorContext actorContext)
    : IRequestHandler<CrearRolloutCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearRolloutCommand request, CancellationToken cancellationToken)
    {
        var flag = await flagRepository.GetByIdAsync(request.FeatureFlagId, cancellationToken);
        if (flag is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "FeatureManagement.Rollouts.FlagNoEncontrado", $"No existe el flag {request.FeatureFlagId}."));
        }

        var segmento = await segmentoRepository.GetByIdAsync(request.SegmentoId, cancellationToken);
        if (segmento is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "FeatureManagement.Rollouts.SegmentoNoEncontrado", $"No existe el segmento {request.SegmentoId}."));
        }

        var yaAsociado = await rolloutRepository.AnyAsync(
            new RolloutPorFlagYSegmentoSpecification(request.FeatureFlagId, request.SegmentoId), cancellationToken);
        if (yaAsociado)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "FeatureManagement.Rollouts.AsociacionDuplicada",
                $"El flag {request.FeatureFlagId} ya tiene un rollout hacia el segmento {request.SegmentoId}."));
        }

        var rollout = new Rollout(Guid.NewGuid(), request.FeatureFlagId, request.SegmentoId);
        await rolloutRepository.AddAsync(rollout, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "featuremanagement.rollouts.crear",
            resource: new AuditResource("featuremanagement.rollouts", rollout.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?>
            {
                ["featureFlagId"] = rollout.FeatureFlagId.ToString(),
                ["segmentoId"] = rollout.SegmentoId.ToString(),
            });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return rollout.Id;
    }
}
