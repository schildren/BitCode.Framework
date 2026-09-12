using BitCode.Framework.Platform.Notifications.Actors;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>
/// Camino HTTP síncrono/directo para disparar una notificación (Fase 6, módulo 8) -- envoltorio delgado
/// sobre <see cref="INotificationSender"/> para un cliente EXTERNO (fuera de este proceso .NET) que
/// necesita disparar una notificación puntual sin publicar un evento de integración. Un módulo alojado
/// en el MISMO proceso (mismo host) debe preferir inyectar <see cref="INotificationSender"/>
/// directamente -- evita el costo de un round-trip HTTP + serialización cuando ambos ya comparten
/// contenedor de DI. Ver "Cómo se dispara una notificación" en <c>docs/guia-notifications.md</c>.
/// </summary>
internal sealed record EnviarNotificacionCommand(
    Guid DestinatarioUserId,
    string? DestinatarioContacto,
    string CodigoPlantilla,
    NotificationChannel Canal,
    string Locale,
    IReadOnlyDictionary<string, string> Datos) : ICommand<Guid>;

internal sealed class EnviarNotificacionCommandValidator : AbstractValidator<EnviarNotificacionCommand>
{
    public EnviarNotificacionCommandValidator()
    {
        RuleFor(c => c.DestinatarioUserId).NotEmpty();
        RuleFor(c => c.CodigoPlantilla).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Locale).NotEmpty().MaximumLength(16);
        RuleFor(c => c.DestinatarioContacto).MaximumLength(256);
    }
}

internal sealed class EnviarNotificacionCommandHandler(INotificationSender sender, INotificationsActorContext actorContext)
    : IRequestHandler<EnviarNotificacionCommand, Result<Guid>>
{
    public Task<Result<Guid>> Handle(EnviarNotificacionCommand request, CancellationToken cancellationToken) =>
        sender.EnviarAsync(
            new EnviarNotificacionRequest(
                request.DestinatarioUserId,
                request.DestinatarioContacto,
                // DisparadoPorUserId: quien invoca ESTE comando HTTP -- distinto de DestinatarioUserId
                // (ver el remarks de Notification): un supervisor puede disparar una notificación hacia
                // OTRO usuario.
                actorContext.GetCurrentUserId(),
                request.CodigoPlantilla,
                request.Canal,
                request.Locale,
                request.Datos),
            cancellationToken);
}
