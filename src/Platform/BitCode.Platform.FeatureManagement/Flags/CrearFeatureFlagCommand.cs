using BitCode.Framework.Platform.FeatureManagement.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

/// <summary>
/// Alta de un flag (nace apagado, ver <see cref="FeatureFlag"/>). Implementa
/// <see cref="IIdempotentCommand"/> (F1-22) -- un POST repetido con la misma Idempotency-Key y el mismo
/// cuerpo no crea un segundo flag.
///
/// Nota de concurrencia (mismo hallazgo documentado en Catalogs and Parameters, Fase 6 módulo 3, para
/// <c>CrearCatalogoCommandHandler</c>): la validación de nombre duplicado (<c>AnyAsync</c>, check) ocurre
/// antes de insertar (act), sin aislamiento serializable ni <c>ITransactionalCommand</c>. El índice único
/// <c>(TenantId, Nombre)</c> de <see cref="FeatureManagementDbContext"/> es el guardrail de datos que
/// evita que dos flags duplicados terminen persistidos bajo una carrera real (a diferencia de
/// <c>ParametroVigencia</c> en Catalogs, que no tiene un guardrail equivalente) -- pero una carrera
/// perdedora recibe una <c>DbUpdateException</c> no traducida a <c>Result.Failure</c>/409 en este primer
/// corte (el <c>ExceptionHandlerMiddleware</c> genérico la convierte en un 500), ver
/// <c>docs/guia-feature-management.md</c>, sección "Pendientes".
/// </summary>
internal sealed record CrearFeatureFlagCommand(string Nombre, string? Descripcion)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearFeatureFlagCommandValidator : AbstractValidator<CrearFeatureFlagCommand>
{
    public CrearFeatureFlagCommandValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Descripcion).MaximumLength(500);
    }
}

/// <summary>
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, docs/convenciones.md):
/// <c>TransactionBehavior</c> lo hace al final del pipeline.
/// </summary>
internal sealed class CrearFeatureFlagCommandHandler(
    IRepository<FeatureFlag, Guid> repository,
    IAuditWriter auditWriter,
    IFeatureManagementActorContext actorContext)
    : IRequestHandler<CrearFeatureFlagCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearFeatureFlagCommand request, CancellationToken cancellationToken)
    {
        var duplicado = await repository.AnyAsync(new FeatureFlagPorNombreSpecification(request.Nombre), cancellationToken);
        if (duplicado)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "FeatureManagement.Flags.NombreDuplicado", $"Ya existe un flag con nombre '{request.Nombre}'."));
        }

        var flag = new FeatureFlag(Guid.NewGuid(), request.Nombre, request.Descripcion);
        await repository.AddAsync(flag, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "featuremanagement.flags.crear",
            resource: new AuditResource("featuremanagement.flags", flag.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["nombre"] = flag.Nombre });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return flag.Id;
    }
}
