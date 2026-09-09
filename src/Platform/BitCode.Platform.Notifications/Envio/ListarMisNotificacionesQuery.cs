using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Envio;

internal sealed record ListarMisNotificacionesQuery(NotificationEstado? Estado, int Page, int PageSize)
    : IQuery<PagedResult<NotificationResponse>>;

internal sealed class ListarMisNotificacionesQueryHandler(
    IReadRepository<Notification, Guid> repository, INotificationsActorContext actorContext)
    : IRequestHandler<ListarMisNotificacionesQuery, Result<PagedResult<NotificationResponse>>>
{
    public async Task<Result<PagedResult<NotificationResponse>>> Handle(
        ListarMisNotificacionesQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<NotificationResponse>>(pageRequestResult.Error);
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<PagedResult<NotificationResponse>>(new Error(
                "Notifications.Notificaciones.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        return await repository.ListPagedAsync(
            new NotificacionesDeDestinatarioSpecification(actorUserId.Value, request.Estado),
            n => new NotificationResponse(
                n.Id, n.DestinatarioUserId, n.DisparadoPorUserId, n.CodigoPlantilla, n.Canal, n.Locale,
                n.Asunto, n.Cuerpo, n.Estado, n.IntentosRealizados, n.UltimoErrorMensaje, n.EnviadaAtUtc, n.LeidoAtUtc),
            pageRequestResult.Value,
            cancellationToken);
    }
}
