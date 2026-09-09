using BitCode.Framework.Platform.Notifications.Envio.Canales;
using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Platform.Notifications.Preferencias;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>
/// Implementación real de <see cref="INotificationSender"/> -- resuelve la plantilla, la renderiza,
/// respeta la preferencia del destinatario ANTES de intentar entregar (Fase 6, módulo 8:
/// "preferencias" del Plan Maestro) y registra tracking de cada intento (<see cref="NotificationDelivery"/>).
/// No llama <c>IUnitOfWork.SaveChangesAsync</c> explícitamente (regla dura 1, docs/convenciones.md) --
/// tanto <c>EnviarNotificacionCommand</c> (vía <c>TransactionBehavior</c>) como
/// <c>IInboxMessageProcessor.ProcessAsync</c> (vía el mismo mecanismo que Task Inbox) son quienes
/// confirman la transacción, según el camino que haya invocado este servicio.
/// </summary>
internal sealed class NotificationSender(
    IRepository<Notification, Guid> notificationRepository,
    IRepository<NotificationDelivery, Guid> deliveryRepository,
    IReadRepository<NotificationTemplate, Guid> templateRepository,
    IReadRepository<UserNotificationPreference, Guid> preferenceRepository,
    IEnumerable<INotificationChannelSender> channelSenders,
    IOptions<NotificationsOptions> options)
    : INotificationSender
{
    public async Task<Result<Guid>> EnviarAsync(EnviarNotificacionRequest request, CancellationToken cancellationToken = default)
    {
        var plantillas = await templateRepository.ListAsync(
            new NotificationTemplateActivaSpecification(request.CodigoPlantilla, request.Canal, request.Locale),
            cancellationToken);
        var plantilla = plantillas.FirstOrDefault();
        if (plantilla is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Notifications.Notificaciones.PlantillaNoEncontrada",
                $"No existe una plantilla activa para código '{request.CodigoPlantilla}', canal {request.Canal} y locale '{request.Locale}'."));
        }

        var asunto = plantilla.Asunto is null ? null : NotificationTemplateRenderer.Render(plantilla.Asunto, request.Datos);
        var cuerpo = NotificationTemplateRenderer.Render(plantilla.Cuerpo, request.Datos);

        var notification = new Notification(
            Guid.NewGuid(), request.DestinatarioUserId, request.DestinatarioContacto, request.DisparadoPorUserId,
            request.CodigoPlantilla, request.Canal, request.Locale, asunto, cuerpo);

        var optoPorNoRecibir = await preferenceRepository.AnyAsync(
            new PreferenciaExactaSpecification(request.DestinatarioUserId, request.CodigoPlantilla, request.Canal),
            cancellationToken);
        if (optoPorNoRecibir)
        {
            notification.MarcarOmitidaPorPreferencia();
            await notificationRepository.AddAsync(notification, cancellationToken);
            await deliveryRepository.AddAsync(
                new NotificationDelivery(
                    Guid.NewGuid(), notification.Id, request.Canal, intentoNumero: 1,
                    NotificationDeliveryResultado.OmitidoPorPreferencia, errorMensaje: null),
                cancellationToken);

            return notification.Id;
        }

        await notificationRepository.AddAsync(notification, cancellationToken);

        var sender = channelSenders.FirstOrDefault(s => s.Canal == request.Canal);
        if (sender is null)
        {
            // Error de configuración del host (no registró un INotificationChannelSender para este
            // canal, ver AddSharedNotifications) -- nunca un dato de negocio inválido, así que no tiene
            // sentido modelarlo como fallo de la propia Notification (no hay ningún intento de entrega
            // que registrar en el tracking): se propaga como Result.Failure de la operación completa,
            // sin persistir la fila a medio construir.
            return Result.Failure<Guid>(Error.Failure(
                "Notifications.Notificaciones.CanalNoRegistrado",
                $"No hay ningún INotificationChannelSender registrado para el canal {request.Canal}."));
        }

        await RegistrarIntentoAsync(notification, sender, cancellationToken);

        return notification.Id;
    }

    /// <summary>Ejecuta UN intento de entrega contra <paramref name="sender"/> y deja
    /// <paramref name="notification"/>/su fila de <see cref="NotificationDelivery"/> correspondiente
    /// consistentes con el resultado. <c>NotificationRetryJob</c> (que corre fuera del pipeline de
    /// MediatR/DI de request, con acceso directo a <c>NotificationsDbContext</c>, excepción legítima
    /// documentada a la regla dura 1/5 -- mismo precedente que <c>WorkflowEscalamientoJob</c>) NO
    /// reutiliza este método: reimplementa la misma lógica de decisión contra el <c>DbContext</c>
    /// directamente, ver su propio código para el detalle.</summary>
    private async Task RegistrarIntentoAsync(
        Notification notification, INotificationChannelSender sender, CancellationToken cancellationToken)
    {
        var intentoNumero = notification.IntentosRealizados + 1;
        var resultado = await sender.SendAsync(notification, cancellationToken);
        var ahoraUtc = DateTime.UtcNow;

        NotificationDeliveryResultado deliveryResultado;
        switch (resultado.Outcome)
        {
            case NotificationSendOutcome.Exitoso:
                notification.RegistrarEnvioExitoso(ahoraUtc);
                deliveryResultado = NotificationDeliveryResultado.Exitoso;
                break;

            case NotificationSendOutcome.FalloPermanente:
                notification.RegistrarEnvioFallidoPermanente(resultado.ErrorMensaje ?? "Fallo permanente sin detalle.");
                deliveryResultado = NotificationDeliveryResultado.FalloPermanente;
                break;

            default:
                notification.RegistrarEnvioFallidoTransitorio(
                    resultado.ErrorMensaje ?? "Fallo transitorio sin detalle.", ahoraUtc, options.Value.Retry);
                deliveryResultado = NotificationDeliveryResultado.FalloTransitorio;
                break;
        }

        // NO se llama a notificationRepository.Update(notification) acá: la fila todavía está en
        // estado Added (recién creada por EnviarAsync, más arriba, dentro de la MISMA unidad de
        // trabajo/SaveChangesAsync) -- llamar a Update() sobre una entidad ya trackeada como Added la
        // fuerza a Modified, lo que le hace generar un UPDATE contra una fila que todavía no existe
        // (0 filas afectadas, ConcurrencyConflictException falsa). El ChangeTracker de EF Core ya
        // captura los cambios hechos por RegistrarEnvioExitoso/RegistrarEnvioFallidoTransitorio/
        // RegistrarEnvioFallidoPermanente sobre la misma instancia sin necesitar ninguna llamada
        // explícita adicional.
        await deliveryRepository.AddAsync(
            new NotificationDelivery(Guid.NewGuid(), notification.Id, sender.Canal, intentoNumero, deliveryResultado, resultado.ErrorMensaje),
            cancellationToken);
    }
}
