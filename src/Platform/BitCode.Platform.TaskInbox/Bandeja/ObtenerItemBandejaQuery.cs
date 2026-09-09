using BitCode.Framework.Platform.TaskInbox.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.TaskInbox.Bandeja;

internal sealed record ObtenerItemBandejaQuery(Guid Id) : IQuery<TaskInboxItemResponse>;

internal sealed class ObtenerItemBandejaQueryHandler(
    IReadRepository<TaskInboxItem, Guid> repository, ITaskInboxActorContext actorContext)
    : IRequestHandler<ObtenerItemBandejaQuery, Result<TaskInboxItemResponse>>
{
    public async Task<Result<TaskInboxItemResponse>> Handle(ObtenerItemBandejaQuery request, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (item is null)
        {
            return Result.Failure<TaskInboxItemResponse>(Error.NotFound(
                "TaskInbox.Bandeja.NoEncontrado", $"No existe el ítem de bandeja {request.Id}."));
        }

        // Ownership: el permiso RBAC (taskinbox.bandeja.ver) solo habilita ver LA PROPIA bandeja, igual
        // que ListarBandejaQuery/MarcarComoLeidaCommand -- sin este chequeo, cualquier actor con el
        // permiso genérico podía leer el ítem de bandeja de otro usuario por id (IDOR, hallazgo de
        // auditoría de arquitectura, 2026-09-09).
        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<TaskInboxItemResponse>(new Error(
                "TaskInbox.Bandeja.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        if (item.AsignadoAUserId != actorUserId.Value)
        {
            return Result.Failure<TaskInboxItemResponse>(new Error(
                "TaskInbox.Bandeja.NoAutorizado",
                "Solo el actor actualmente asignado puede ver este ítem de bandeja.", ErrorType.Forbidden));
        }

        return Result.Success(new TaskInboxItemResponse(
            item.Id, item.WorkflowInstanceId, item.AsignadoAUserId, item.Estado, item.AsignadaAtUtc,
            item.ResueltaPorUserId, item.ResueltaAtUtc, item.LeidoAtUtc));
    }
}
