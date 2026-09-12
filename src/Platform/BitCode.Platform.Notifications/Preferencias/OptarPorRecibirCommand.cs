using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Preferencias;

/// <summary>Reverso de <see cref="OptarPorNoRecibirCommand"/> -- borra la fila de opt-out del ACTOR
/// AUTENTICADO si existe. Idempotente: pedir opt-in de algo de lo que no se había optado por no recibir
/// no falla.</summary>
internal sealed record OptarPorRecibirCommand(string CodigoPlantilla, NotificationChannel Canal) : ICommand;

internal sealed class OptarPorRecibirCommandValidator : AbstractValidator<OptarPorRecibirCommand>
{
    public OptarPorRecibirCommandValidator() => RuleFor(c => c.CodigoPlantilla).NotEmpty().MaximumLength(128);
}

internal sealed class OptarPorRecibirCommandHandler(
    IRepository<UserNotificationPreference, Guid> repository, INotificationsActorContext actorContext)
    : IRequestHandler<OptarPorRecibirCommand, Result>
{
    public async Task<Result> Handle(OptarPorRecibirCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = actorContext.GetCurrentUserId();
        if (actorUserId is null)
        {
            return Result.Failure(new Error(
                "Notifications.Preferencias.ActorNoResuelto", "No se pudo resolver el actor autenticado.", ErrorType.Forbidden));
        }

        var existentes = await repository.ListAsync(
            new PreferenciaExactaSpecification(actorUserId.Value, request.CodigoPlantilla, request.Canal),
            cancellationToken);

        foreach (var preferencia in existentes)
        {
            repository.Remove(preferencia);
        }

        return Result.Success();
    }
}
