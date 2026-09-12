using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>Bandeja mínima de un actor -- solo devuelve SUS propias tareas pendientes (nunca recibe un
/// <c>AsignadoAUserId</c> como parámetro del cliente: siempre se resuelve del actor autenticado, para que
/// un usuario no pueda listar la bandeja de otro leyendo la query directamente).</summary>
internal sealed record ListarTareasPendientesQuery(int Page, int PageSize) : IQuery<PagedResult<WorkflowTaskResponse>>;

internal sealed class ListarTareasPendientesQueryHandler(
    IReadRepository<WorkflowTask, Guid> repository, Actors.IWorkflowActorContext actorContext)
    : IRequestHandler<ListarTareasPendientesQuery, Result<PagedResult<WorkflowTaskResponse>>>
{
    public async Task<Result<PagedResult<WorkflowTaskResponse>>> Handle(
        ListarTareasPendientesQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<WorkflowTaskResponse>>(pageRequestResult.Error);
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<PagedResult<WorkflowTaskResponse>>(new Error(
                "Workflow.Tareas.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        return await repository.ListPagedAsync(
            new TareasPendientesDeActorSpecification(actorUserId.Value),
            t => new WorkflowTaskResponse(
                t.Id, t.WorkflowInstanceId, t.WorkflowStateId, t.Titulo, t.AsignadoAUserId, t.Estado, t.AccionResuelta,
                t.SlaVencimientoUtc, t.Escalada),
            pageRequestResult.Value,
            cancellationToken);
    }
}
