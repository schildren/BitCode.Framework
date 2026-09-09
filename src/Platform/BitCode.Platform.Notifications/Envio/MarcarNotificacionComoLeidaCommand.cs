using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>Marca la propia notificación como leída -- con sentido real solo para el canal InApp (ver
/// <see cref="Notification.LeidoAtUtc"/>), pero no se restringe por canal: marcar como leído un email ya
/// entregado es inofensivo, simplemente no tiene ninguna consecuencia visible para ese canal.</summary>
internal sealed record MarcarNotificacionComoLeidaCommand(Guid Id) : ICommand;

internal sealed class MarcarNotificacionComoLeidaCommandValidator : AbstractValidator<MarcarNotificacionComoLeidaCommand>
{
    public MarcarNotificacionComoLeidaCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class MarcarNotificacionComoLeidaCommandHandler(
    IRepository<Notification, Guid> repository, INotificationsActorContext actorContext)
    : IRequestHandler<MarcarNotificacionComoLeidaCommand, Result>
{
    public async Task<Result> Handle(MarcarNotificacionComoLeidaCommand request, CancellationToken cancellationToken)
    {
        var notification = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (notification is null)
        {
            return Result.Failure(Error.NotFound(
                "Notifications.Notificaciones.NoEncontrada", $"No existe la notificación {request.Id}."));
        }

        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure(new Error(
                "Notifications.Notificaciones.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        var result = notification.MarcarComoLeida(actorUserId.Value);
        if (result.IsFailure)
        {
            return result;
        }

        repository.Update(notification);
        return Result.Success();
    }
}
