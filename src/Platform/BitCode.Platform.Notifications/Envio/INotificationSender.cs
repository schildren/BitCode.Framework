using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>
/// Solicitud de disparo de una notificación -- el mismo contrato que usan tanto
/// <c>EnviarNotificacionCommand</c> (camino HTTP síncrono/directo) como cualquier
/// <c>IEventConsumer&lt;TEvent&gt;</c> de este módulo (camino asíncrono vía evento de integración, ver
/// <c>Eventos/TareaAsignadaNotificationEventConsumer.cs</c>).
/// </summary>
/// <param name="DestinatarioUserId">Quien RECIBE la notificación.</param>
/// <param name="DestinatarioContacto">Dirección de contacto específica del canal (email para
/// <see cref="NotificationChannel.Email"/>); <see langword="null"/> para <see cref="NotificationChannel.InApp"/>.</param>
/// <param name="DisparadoPorUserId">Quien DISPARÓ la notificación mediante una acción humana explícita
/// -- <see langword="null"/> cuando el origen es un consumidor de eventos de integración (no hay ningún
/// actor humano en ese camino). DELIBERADAMENTE un parámetro distinto de
/// <paramref name="DestinatarioUserId"/>, nunca el mismo valor reutilizado por conveniencia -- ver el
/// <c>remarks</c> de <see cref="Notification"/>.</param>
public sealed record EnviarNotificacionRequest(
    Guid DestinatarioUserId,
    string? DestinatarioContacto,
    Guid? DisparadoPorUserId,
    string CodigoPlantilla,
    NotificationChannel Canal,
    string Locale,
    IReadOnlyDictionary<string, string> Datos);

/// <summary>
/// Punto de entrada PROGRAMÁTICO (in-process) para disparar una notificación sin pasar por HTTP -- para
/// que otro bounded context alojado en el MISMO proceso pueda inyectar esta interfaz directamente y
/// disparar una notificación de forma síncrona (ver "Cómo se dispara una notificación" en
/// <c>docs/guia-notifications.md</c> para cuándo usar este camino frente al de eventos de integración).
/// Esta es la superficie pública reutilizable del módulo -- <c>EnviarNotificacionCommand</c> (el
/// endpoint HTTP) es apenas un envoltorio delgado sobre esta misma interfaz para el caso "un cliente
/// HTTP externo dispara una notificación".
/// </summary>
public interface INotificationSender
{
    Task<Result<Guid>> EnviarAsync(EnviarNotificacionRequest request, CancellationToken cancellationToken = default);
}
