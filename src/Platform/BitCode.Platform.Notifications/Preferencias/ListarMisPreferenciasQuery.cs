using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Preferencias;

internal sealed record UserNotificationPreferenceResponse(Guid Id, string CodigoPlantilla, Plantillas.NotificationChannel Canal);

internal sealed record ListarMisPreferenciasQuery : IQuery<IReadOnlyList<UserNotificationPreferenceResponse>>;

internal sealed class ListarMisPreferenciasQueryHandler(
    IReadRepository<UserNotificationPreference, Guid> repository, INotificationsActorContext actorContext)
    : IRequestHandler<ListarMisPreferenciasQuery, Result<IReadOnlyList<UserNotificationPreferenceResponse>>>
{
    public async Task<Result<IReadOnlyList<UserNotificationPreferenceResponse>>> Handle(
        ListarMisPreferenciasQuery request, CancellationToken cancellationToken)
    {
        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure<IReadOnlyList<UserNotificationPreferenceResponse>>(new Error(
                "Notifications.Preferencias.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        var items = await repository.ListAsync(
            new PreferenciasDeActorSpecification(actorUserId.Value),
            p => new UserNotificationPreferenceResponse(p.Id, p.CodigoPlantilla, p.Canal),
            cancellationToken);

        return Result.Success(items);
    }
}
