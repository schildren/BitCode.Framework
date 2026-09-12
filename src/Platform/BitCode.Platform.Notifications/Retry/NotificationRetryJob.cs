using BitCode.Framework.Platform.Notifications.Envio;
using BitCode.Framework.Platform.Notifications.Envio.Canales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace BitCode.Framework.Platform.Notifications.Retry;

/// <summary>
/// Reintenta periódicamente las <see cref="Notification"/> en estado
/// <see cref="NotificationEstado.PendienteDeReintento"/> cuyo <see cref="Notification.ProximoReintentoUtc"/>
/// ya venció (Fase 6, módulo 8: "retry" del Plan Maestro) -- un consumidor real lo registra con
/// <c>AddSharedBackgroundJobs</c> (F4-11, Quartz HA, ver <c>docs/guia-quartz-ha.md</c>) para que un job
/// lógico se dispare una única vez entre N réplicas, mismo patrón exacto que
/// <c>WorkflowEscalamientoJob</c> (Fase 6, módulo 6):
/// <code>
/// services.AddSharedBackgroundJobs(
///     quartz =>
///     {
///         var jobKey = new JobKey("notifications-retry");
///         quartz.AddJob&lt;NotificationRetryJob&gt;(j => j.WithIdentity(jobKey).RequestRecovery().StoreDurably());
///         quartz.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInSeconds(30).RepeatForever()));
///     },
///     ha => ha.ConnectionString = connectionString);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>Backoff reutilizado, no reinventado (ver Plan Maestro, sección "Cómo se dispara una
/// notificación" de este módulo):</b> el "cuándo" de cada reintento (<see cref="Notification.ProximoReintentoUtc"/>)
/// ya lo calculó <see cref="Notification.RegistrarEnvioFallidoTransitorio"/> con
/// <c>EventRetryBackoff.CalculateDelay</c> (F3-07) en el momento del fallo -- este job solo necesita
/// preguntar "¿ya pasó esa fecha?", exactamente el mismo diseño que <c>OutboxMessage.LockedUntilUtc</c>/
/// <c>OutboxBatchProcessor</c> (F3-03/F3-07), sin ninguna cola/scheduler propio: Quartz HA (F4-11) es el
/// único mecanismo de "disparar periódicamente" que este módulo introduce, reutilizando la misma
/// infraestructura compartida que Workflow.
/// </para>
/// <para>
/// Acceso directo a <see cref="NotificationsDbContext"/> con <c>IgnoreQueryFilters()</c> -- excepción
/// legítima documentada a la regla dura 1/5 (docs/convenciones.md), mismo precedente que
/// <c>WorkflowEscalamientoJob</c>/<c>OutboxBatchProcessor</c>/<c>EfIdempotencyStore</c>: este job es un
/// worker de infraestructura, no un handler de comando dentro del pipeline de MediatR/
/// <c>TransactionBehavior</c>, y necesita ver notificaciones pendientes de TODOS los tenants en cada
/// disparo. Llama <c>SaveChangesAsync</c> explícitamente por la misma razón.
/// </para>
/// <para>
/// <b>Sin bloqueo entre réplicas dentro del mismo ciclo</b> (a diferencia de <c>OutboxBatchProcessor</c>,
/// que sí usa <c>UPDLOCK, ROWLOCK, READPAST</c> porque corre como <c>BackgroundService</c> standalone,
/// potencialmente varias instancias sondeando en simultáneo): Quartz HA (F4-11) ya garantiza que el JOB
/// LÓGICO completo se ejecuta una única vez entre réplicas en cada disparo del trigger (lock a nivel de
/// job, no de fila) -- el mismo criterio que <c>WorkflowEscalamientoJob</c> ya aplica, sin necesitar un
/// segundo mecanismo de bloqueo a nivel de fila.
/// </para>
/// <para>
/// <b>Idempotente por diseño (regla dura 28):</b> si el proceso muere a mitad de este método (por
/// ejemplo, después de reintentar la notificación N pero antes del <c>SaveChangesAsync</c> final), la
/// próxima ejecución (<c>RequestRecovery()</c>) vuelve a intentar la MISMA fila (que sigue en
/// <see cref="NotificationEstado.PendienteDeReintento"/> con el <see cref="Notification.ProximoReintentoUtc"/>
/// ya vencido) -- un reintento duplicado contra el canal real es un duplicado aceptable (semántica
/// "at-least-once", nunca exactly-once de punta a punta, Plan Maestro sección 3.2), no una corrupción de
/// datos: el peor caso es que el destinatario reciba el mismo email/notificación InApp dos veces.
/// </para>
/// </remarks>
public sealed class NotificationRetryJob(
    NotificationsDbContext dbContext, IEnumerable<INotificationChannelSender> channelSenders, IOptions<NotificationsOptions> options)
    : IJob
{
    public Task Execute(IJobExecutionContext context) => ReintentarPendientesAsync(context.CancellationToken);

    /// <summary>Lógica real del job, separada de <see cref="Execute"/> para poder probarla sin construir
    /// un <see cref="IJobExecutionContext"/> real de Quartz -- mismo criterio que
    /// <c>WorkflowEscalamientoJob.EscalarVencidasAsync</c>.</summary>
    public async Task ReintentarPendientesAsync(CancellationToken cancellationToken)
    {
        var ahoraUtc = DateTime.UtcNow;

        var pendientes = await dbContext.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.Estado == NotificationEstado.PendienteDeReintento
                        && n.ProximoReintentoUtc != null && n.ProximoReintentoUtc <= ahoraUtc)
            .ToListAsync(cancellationToken);

        if (pendientes.Count == 0)
        {
            return;
        }

        foreach (var notification in pendientes)
        {
            var sender = channelSenders.FirstOrDefault(s => s.Canal == notification.Canal);
            if (sender is null)
            {
                // Ningún INotificationChannelSender registrado para este canal (problema de
                // configuración del host, no un dato de negocio inválido de esta fila en particular) --
                // se salta sin consumir un intento, para no penalizar a la notificación por un error
                // ajeno a ella; el próximo ciclo la vuelve a intentar si para entonces el host ya
                // registró el canal faltante.
                continue;
            }

            var intentoNumero = notification.IntentosRealizados + 1;
            var timestampUtc = DateTime.UtcNow;

            NotificationDeliveryResultado deliveryResultado;
            string? errorMensaje;
            try
            {
                var resultado = await sender.SendAsync(notification, cancellationToken);
                errorMensaje = resultado.ErrorMensaje;

                switch (resultado.Outcome)
                {
                    case NotificationSendOutcome.Exitoso:
                        notification.RegistrarEnvioExitoso(timestampUtc);
                        deliveryResultado = NotificationDeliveryResultado.Exitoso;
                        break;

                    case NotificationSendOutcome.FalloPermanente:
                        notification.RegistrarEnvioFallidoPermanente(errorMensaje ?? "Fallo permanente sin detalle.");
                        deliveryResultado = NotificationDeliveryResultado.FalloPermanente;
                        break;

                    default:
                        notification.RegistrarEnvioFallidoTransitorio(
                            errorMensaje ?? "Fallo transitorio sin detalle.", timestampUtc, options.Value.Retry);
                        deliveryResultado = NotificationDeliveryResultado.FalloTransitorio;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Aislamiento por ítem (hallazgo Alto de auditoría de arquitectura, 2026-09-09): un
                // INotificationChannelSender que lanza en vez de devolver un NotificationSendResult
                // clasificado (ej. un canal de un consumidor real, no los dos de referencia) NO debe
                // abortar el foreach completo -- antes, esa excepción escapaba hasta afuera del método y
                // el único SaveChangesAsync final (al terminar el lote) nunca se ejecutaba, perdiendo en
                // silencio el progreso de TODAS las notificaciones ya procesadas en este mismo ciclo, no
                // solo la que falló. Se trata como transitorio (mismo criterio conservador que el catch-
                // all de EmailNotificationChannelSender): ante la duda, reintentar es más seguro que
                // descartar definitivamente un envío legítimo.
                errorMensaje = ex.Message;
                notification.RegistrarEnvioFallidoTransitorio(errorMensaje, timestampUtc, options.Value.Retry);
                deliveryResultado = NotificationDeliveryResultado.FalloTransitorio;
            }

            // TenantId asignado explícitamente (no vía TenantSaveChangesInterceptor, que no tiene
            // ningún tenant "actual" que asumir en un job cross-tenant sin HttpContext) -- se toma del
            // mismo tenant que la notificación reintentada, leída con IgnoreQueryFilters más arriba.
            // Mismo criterio que WorkflowEscalamientoJob con WorkflowHistorial.
            dbContext.NotificationDeliveries.Add(new NotificationDelivery(
                Guid.NewGuid(), notification.Id, notification.Canal, intentoNumero, deliveryResultado, errorMensaje)
            {
                TenantId = notification.TenantId,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
