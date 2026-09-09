using BitCode.Framework.Platform.Workflow.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>
/// Alta de workflow (identidad estable, Fase 6, módulo Workflow). Implementa
/// <see cref="IIdempotentCommand"/> (F1-22) -- un POST repetido con la misma Idempotency-Key y el mismo
/// cuerpo no crea un segundo workflow.
/// </summary>
internal sealed record CrearWorkflowDefinitionCommand(string Codigo, string Nombre, string? Descripcion)
    : ICommand<Guid>, IIdempotentCommand;

internal sealed class CrearWorkflowDefinitionCommandValidator : AbstractValidator<CrearWorkflowDefinitionCommand>
{
    public CrearWorkflowDefinitionCommandValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Descripcion).MaximumLength(500);
    }
}

internal sealed class CrearWorkflowDefinitionCommandHandler(
    IRepository<WorkflowDefinition, Guid> repository,
    IAuditWriter auditWriter,
    IWorkflowActorContext actorContext)
    : IRequestHandler<CrearWorkflowDefinitionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearWorkflowDefinitionCommand request, CancellationToken cancellationToken)
    {
        var duplicado = await repository.AnyAsync(
            new WorkflowDefinitionPorCodigoSpecification(request.Codigo), cancellationToken);
        if (duplicado)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Workflow.Definiciones.CodigoDuplicado", $"Ya existe un workflow con código '{request.Codigo}'."));
        }

        var definicion = new WorkflowDefinition(Guid.NewGuid(), request.Codigo, request.Nombre, request.Descripcion);
        await repository.AddAsync(definicion, cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: null,
            action: "workflow.definiciones.crear",
            resource: new AuditResource("workflow.definiciones", definicion.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["codigo"] = definicion.Codigo });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return definicion.Id;
    }
}
