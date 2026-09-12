using BitCode.Framework.Platform.TaskInbox.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.TaskInbox.Bandeja;

/// <summary>
/// La bandeja enriquecida de un actor (Fase 6, módulo 7: "bandeja" + "filtros" del Plan Maestro) --
/// equivalente, en espíritu, a <c>ListarTareasPendientesQuery</c> de Workflow, pero contra el
/// read-model propio de este módulo (así puede ofrecer filtros por estado/instancia/rango de fechas
/// sin tocar <c>WorkflowDbContext</c>) y sin la restricción de "solo pendientes" -- un actor puede
/// filtrar también su historial de tareas ya aprobadas/rechazadas.
/// </summary>
internal sealed record ListarBandejaQuery(
    TaskInboxEstado? Estado, Guid? WorkflowInstanceId, DateTime? DesdeUtc, DateTime? HastaUtc, int Page, int PageSize)
    : IQuery<PagedResult<TaskInboxItemResponse>>;

internal sealed class ListarBandejaQueryHandler(
    IReadRepository<TaskInboxItem, Guid> repository, ITaskInboxActorContext actorContext)
    : IRequestHandler<ListarBandejaQuery, Result<PagedResult<TaskInboxItemResponse>>>
{
    public async Task<Result<PagedResult<TaskInboxItemResponse>>> Handle(
        ListarBandejaQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<TaskInboxItemResponse>>(pageRequestResult.Error);
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<PagedResult<TaskInboxItemResponse>>(new Error(
                "TaskInbox.Bandeja.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        var specification = new BandejaDeActorSpecification(
            actorUserId.Value, request.Estado, request.WorkflowInstanceId, request.DesdeUtc, request.HastaUtc);

        return await repository.ListPagedAsync(
            specification,
            i => new TaskInboxItemResponse(
                i.Id, i.WorkflowInstanceId, i.AsignadoAUserId, i.Estado, i.AsignadaAtUtc,
                i.ResueltaPorUserId, i.ResueltaAtUtc, i.LeidoAtUtc),
            pageRequestResult.Value,
            cancellationToken);
    }
}
