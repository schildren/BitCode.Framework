using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>
/// Detalle de UNA notificación -- ownership verificado EXPLÍCITAMENTE en el handler (no solo el permiso
/// RBAC genérico <see cref="NotificationsPermissions.NotificacionesVer"/>), aplicando desde el diseño
/// inicial la misma corrección que Task Inbox tuvo que aplicar recién tras una auditoría de arquitectura
/// (hallazgo Alto, IDOR, 2026-09-09, <c>docs/guia-taskinbox.md</c>): sin este chequeo, cualquier actor
/// con el permiso genérico podría leer la notificación de OTRO usuario por id. Solo el destinatario o
/// quien la disparó pueden verla.
/// </summary>
internal sealed record ObtenerNotificacionQuery(Guid Id) : IQuery<NotificationResponse>;

internal sealed class ObtenerNotificacionQueryHandler(
    IReadRepository<Notification, Guid> repository, INotificationsActorContext actorContext)
    : IRequestHandler<ObtenerNotificacionQuery, Result<NotificationResponse>>
{
    public async Task<Result<NotificationResponse>> Handle(ObtenerNotificacionQuery request, CancellationToken cancellationToken)
    {
        var notification = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (notification is null)
        {
            return Result.Failure<NotificationResponse>(Error.NotFound(
                "Notifications.Notificaciones.NoEncontrada", $"No existe la notificación {request.Id}."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<NotificationResponse>(new Error(
                "Notifications.Notificaciones.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        if (notification.DestinatarioUserId != actorUserId.Value && notification.DisparadoPorUserId != actorUserId.Value)
        {
            return Result.Failure<NotificationResponse>(new Error(
                "Notifications.Notificaciones.NoAutorizado",
                "Solo el destinatario o quien disparó la notificación pueden verla.", ErrorType.Forbidden));
        }

        return Result.Success(Map(notification));
    }

    internal static NotificationResponse Map(Notification notification) => new(
        notification.Id, notification.DestinatarioUserId, notification.DisparadoPorUserId, notification.CodigoPlantilla,
        notification.Canal, notification.Locale, notification.Asunto, notification.Cuerpo, notification.Estado,
        notification.IntentosRealizados, notification.UltimoErrorMensaje, notification.EnviadaAtUtc, notification.LeidoAtUtc);
}
