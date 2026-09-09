using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Preferencias;

/// <summary>
/// Opt-out del ACTOR AUTENTICADO para un tipo de notificación + canal -- nunca recibe el <c>UserId</c>
/// de otro usuario como parámetro (mismo criterio de seguridad que <c>MarcarComoLeidaCommand</c> de Task
/// Inbox): no hay ningún endpoint en este módulo para que un actor administre las preferencias de OTRO
/// usuario. Idempotente: pedir opt-out de algo de lo que ya se optó por no recibir no falla ni duplica
/// la fila.
/// </summary>
internal sealed record OptarPorNoRecibirCommand(string CodigoPlantilla, NotificationChannel Canal) : ICommand;

internal sealed class OptarPorNoRecibirCommandValidator : AbstractValidator<OptarPorNoRecibirCommand>
{
    public OptarPorNoRecibirCommandValidator() => RuleFor(c => c.CodigoPlantilla).NotEmpty().MaximumLength(128);
}

internal sealed class OptarPorNoRecibirCommandHandler(
    IRepository<UserNotificationPreference, Guid> repository, INotificationsActorContext actorContext)
    : IRequestHandler<OptarPorNoRecibirCommand, Result>
{
    public async Task<Result> Handle(OptarPorNoRecibirCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure(new Error(
                "Notifications.Preferencias.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        var yaExiste = await repository.AnyAsync(
            new PreferenciaExactaSpecification(actorUserId.Value, request.CodigoPlantilla, request.Canal),
            cancellationToken);
        if (yaExiste)
        {
            return Result.Success();
        }

        await repository.AddAsync(
            new UserNotificationPreference(Guid.NewGuid(), actorUserId.Value, request.CodigoPlantilla, request.Canal),
            cancellationToken);

        return Result.Success();
    }
}
