using BitCode.Framework.Platform.Workflow.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>
/// Resuelve una <see cref="WorkflowTask"/> pendiente con una acción concreta (Fase 6, "Aprobación y
/// rechazo") y dispara la transición correspondiente de la instancia -- ambos efectos (resolver la tarea,
/// avanzar la instancia) ocurren en el mismo <c>SaveChangesAsync</c> que <c>TransactionBehavior</c> emite
/// al final del pipeline (regla dura 1, docs/convenciones.md).
/// <para>
/// **Control de ownership (checklist Fase 6, requisito común 7 "RBAC y ABAC en operaciones sensibles"):**
/// el endpoint exige el permiso RBAC <see cref="WorkflowPermissions.TareasResolver"/> vía
/// <c>RequireAuthorization</c> (cualquier usuario con ese permiso puede intentar resolver tareas), pero el
/// handler ADEMÁS verifica explícitamente que el actor autenticado (<see cref="IWorkflowActorContext.GetCurrentUserId"/>)
/// sea el <see cref="WorkflowTask.AsignadoAUserId"/> ACTUAL de la tarea -- sin esta segunda verificación,
/// cualquier usuario con el permiso podría resolver la tarea de otro, violando el criterio de aceptación
/// explícito del gate de Fase 6 ("resolver una tarea ajena sin ser el asignado -&gt; denegado"). Es una
/// verificación de ATRIBUTO del recurso (comparar <c>AsignadoAUserId</c> contra el actor), no una regla
/// ABAC configurable vía <c>IAuthorizationPolicyEvaluator</c> (a diferencia de
/// <c>PublicarCatalogoVersionCommandHandler</c>, Catalogs) -- se implementa directamente en el handler
/// porque la relación "asignado == actor" es intrínseca al agregado, no un alcance de negocio configurable
/// por tenant (ver <c>docs/guia-workflow.md</c>, sección "RBAC y ownership").
/// </para>
/// </summary>
internal sealed record ResolverTareaCommand(Guid WorkflowTaskId, string Accion, string? Comentario)
    : ICommand, IIdempotentCommand;

internal sealed class ResolverTareaCommandValidator : AbstractValidator<ResolverTareaCommand>
{
    public ResolverTareaCommandValidator()
    {
        RuleFor(c => c.WorkflowTaskId).NotEmpty();
        RuleFor(c => c.Accion).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Comentario).MaximumLength(1000);
    }
}

internal sealed class ResolverTareaCommandHandler(
    IRepository<WorkflowTask, Guid> taskRepository,
    IRepository<WorkflowInstance, Guid> instanceRepository,
    IRepository<WorkflowHistorial, Guid> historialRepository,
    IWorkflowEngine engine,
    IAuditWriter auditWriter,
    IWorkflowActorContext actorContext)
    : IRequestHandler<ResolverTareaCommand, Result>
{
    public async Task<Result> Handle(ResolverTareaCommand request, CancellationToken cancellationToken)
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
            await WriteAuditEntryAsync(tarea, AuditOutcome.Denied, "El actor no es el asignado actual de la tarea.", cancellationToken);
            return Result.Failure(new Error(
                "Workflow.Tareas.NoAutorizado",
                "Solo el actor actualmente asignado puede resolver esta tarea.", ErrorType.Forbidden));
        }

        var resolverResult = tarea.Resolver(request.Accion, actorUserId.Value, request.Comentario);
        if (resolverResult.IsFailure)
        {
            return resolverResult;
        }

        taskRepository.Update(tarea);

        var instancia = await instanceRepository.GetByIdAsync(tarea.WorkflowInstanceId, cancellationToken);
        if (instancia is null)
        {
            return Result.Failure(Error.NotFound(
                "Workflow.Instancias.NoEncontrada", $"No existe la instancia {tarea.WorkflowInstanceId}."));
        }

        await historialRepository.AddAsync(
            new WorkflowHistorial(Guid.NewGuid(), instancia.Id, $"Tarea{request.Accion}",
                $"Tarea '{tarea.Titulo}' resuelta con acción '{request.Accion}'.", actorUserId.Value),
            cancellationToken);

        var avanceResult = await engine.AvanzarAsync(instancia, request.Accion, cancellationToken);
        if (avanceResult.IsFailure)
        {
            return avanceResult;
        }

        instanceRepository.Update(instancia);

        await WriteAuditEntryAsync(tarea, AuditOutcome.Success, null, cancellationToken);

        return Result.Success();
    }

    private async Task WriteAuditEntryAsync(WorkflowTask tarea, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        var auditRequest = new AuditEntryRequest(
            actor: actorContext.GetCurrentActor(),
            tenantId: tarea.TenantId == Guid.Empty ? null : tarea.TenantId,
            action: "workflow.tareas.resolver",
            resource: new AuditResource("workflow.tareas", tarea.Id.ToString()),
            outcome: outcome,
            reason: reason,
            metadata: new Dictionary<string, string?> { ["workflowInstanceId"] = tarea.WorkflowInstanceId.ToString() });
        _ = await auditWriter.WriteAsync(auditRequest, cancellationToken);
    }
}
