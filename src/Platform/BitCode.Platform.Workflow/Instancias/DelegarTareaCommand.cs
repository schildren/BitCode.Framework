using BitCode.Framework.Platform.Workflow.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>
/// Delegación de una <see cref="WorkflowTask"/> pendiente a otro actor (Fase 6, "Delegación y
/// escalamiento") -- implementada de forma autónoma en este módulo (sin esperar la delegación de Identity
/// Administration, Fase 6 módulo 1, que quedó pendiente en esa tarea): son conceptos relacionados pero
/// independientes -- Identity Administration delegaría PERMISOS de un usuario a otro con alcance amplio,
/// esto delega la responsabilidad de UNA tarea puntual, sin tocar RBAC. Mismo control de ownership que
/// <see cref="ResolverTareaCommand"/>: solo el asignado actual puede delegar.
/// </summary>
internal sealed record DelegarTareaCommand(Guid WorkflowTaskId, Guid NuevoAsignadoUserId) : ICommand, IIdempotentCommand;

internal sealed class DelegarTareaCommandValidator : AbstractValidator<DelegarTareaCommand>
{
    public DelegarTareaCommandValidator()
    {
        RuleFor(c => c.WorkflowTaskId).NotEmpty();
        RuleFor(c => c.NuevoAsignadoUserId).NotEmpty();
    }
}

internal sealed class DelegarTareaCommandHandler(
    IRepository<WorkflowTask, Guid> taskRepository,
    IRepository<WorkflowHistorial, Guid> historialRepository,
    IAuditWriter auditWriter,
    IWorkflowActorContext actorContext)
    : IRequestHandler<DelegarTareaCommand, Result>
{
    public async Task<Result> Handle(DelegarTareaCommand request, CancellationToken cancellationToken)
    {
        var tarea = await taskRepository.GetByIdAsync(request.WorkflowTaskId, cancellationToken);
        if (tarea is null)
        {
            return Result.Failure(Error.NotFound(
                "Workflow.Tareas.NoEncontrada", $"No existe la tarea {request.WorkflowTaskId}."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure(new Error(
                "Workflow.Tareas.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        if (tarea.AsignadoAUserId != actorUserId.Value)
        {
            var auditDenied = new AuditEntryRequest(
                actor: actorContext.GetCurrentActor(),
                tenantId: tarea.TenantId == Guid.Empty ? null : tarea.TenantId,
                action: "workflow.tareas.delegar",
                resource: new AuditResource("workflow.tareas", tarea.Id.ToString()),
                outcome: AuditOutcome.Denied,
                reason: "El actor no es el asignado actual de la tarea.",
                metadata: null);
            _ = await auditWriter.WriteAsync(auditDenied, cancellationToken);

            return Result.Failure(new Error(
                "Workflow.Tareas.NoAutorizado",
                "Solo el actor actualmente asignado puede delegar esta tarea.", ErrorType.Forbidden));
        }

        var delegarResult = tarea.Delegar(request.NuevoAsignadoUserId);
        if (delegarResult.IsFailure)
        {
            return delegarResult;
        }

        taskRepository.Update(tarea);

        await historialRepository.AddAsync(
            new WorkflowHistorial(Guid.NewGuid(), tarea.WorkflowInstanceId, "TareaDelegada",
                $"Tarea '{tarea.Titulo}' delegada de {actorUserId.Value} a {request.NuevoAsignadoUserId}.", actorUserId.Value),
            cancellationToken);

        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: tarea.TenantId == Guid.Empty ? null : tarea.TenantId,
            action: "workflow.tareas.delegar",
            resource: new AuditResource("workflow.tareas", tarea.Id.ToString()),
            outcome: AuditOutcome.Success,
            reason: null,
            metadata: new Dictionary<string, string?> { ["nuevoAsignadoUserId"] = request.NuevoAsignadoUserId.ToString() });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);

        return Result.Success();
    }
}
