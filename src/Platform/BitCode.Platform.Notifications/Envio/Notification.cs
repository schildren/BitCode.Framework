using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Notifications.Envio;

/// <summary>
/// Una notificación disparada -- ya renderizada (<see cref="Asunto"/>/<see cref="Cuerpo"/> son el
/// resultado de <c>NotificationTemplateRenderer.Render</c>, no la plantilla cruda) y con su estado de
/// entrega (Fase 6, módulo 8: "tracking" del Plan Maestro, ver también <see cref="NotificationDelivery"/>
/// para el historial de cada intento).
/// </summary>
/// <remarks>
/// <see cref="DestinatarioUserId"/> (quien RECIBE la notificación) y <see cref="DisparadoPorUserId"/>
/// (quien la DISPARÓ, si corresponde a una acción humana explícita vía
/// <c>EnviarNotificacionCommand</c>/<see cref="Envio.INotificationSender"/>) son DELIBERADAMENTE dos
/// campos distintos, nunca conflacionados -- mismo hallazgo Crítico de auditoría de arquitectura que ya
/// corrigió Task Inbox (2026-09-09, <c>TaskInboxItem.CrearYaResuelta</c>): quien dispara una notificación
/// (un supervisor que fuerza un reenvío, o el propio sistema al consumir un evento) NO es
/// necesariamente su destinatario. <see cref="DisparadoPorUserId"/> es <see langword="null"/> cuando el
/// origen es un consumidor de eventos de integración (no hay ningún actor humano en ese camino, ver
/// <c>Eventos/TareaAsignadaNotificationEventConsumer.cs</c>).
///
/// Implementa <see cref="IHasConcurrencyToken"/> (F1-08) por el mismo motivo que <c>TaskInboxItem</c>: dos
/// caminos independientes pueden mutar la misma fila sin coordinación entre sí -- el intento síncrono
/// original (<c>NotificationSender.EnviarAsync</c>) y <c>NotificationRetryJob</c> (asíncrono, Quartz HA)
/// reintentando esa misma fila más tarde. Sin el token, un reintento del job que se solapara con una
/// consulta/lectura no perdería datos por sí solo, pero si en el futuro se agrega alguna mutación
/// adicional desde el lado HTTP (por ejemplo, "cancelar reintentos"), la falta de este token sería
/// exactamente el mismo lost-update ya documentado para Task Inbox -- se agrega proactivamente desde el
/// diseño inicial, no como corrección posterior.
/// </remarks>
public sealed class Notification : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    public Guid DestinatarioUserId { get; private set; }

    /// <summary>Dirección de contacto específica del canal (por ejemplo, el email para
    /// <see cref="NotificationChannel.Email"/>) -- <see langword="null"/> para <see cref="NotificationChannel.InApp"/>,
    /// que no la necesita. Este módulo NO resuelve el contacto de un <see cref="DestinatarioUserId"/> por
    /// su cuenta (no tiene acceso a Identity Administration) -- debe proveerlo el llamador. Ver
    /// "Canales" en <c>docs/guia-notifications.md</c>.</summary>
    public string? DestinatarioContacto { get; private set; }

    public Guid? DisparadoPorUserId { get; private set; }

    public string CodigoPlantilla { get; private set; } = string.Empty;

    public NotificationChannel Canal { get; private set; }

    public string Locale { get; private set; } = string.Empty;

    public string? Asunto { get; private set; }

    public string Cuerpo { get; private set; } = string.Empty;

    public NotificationEstado Estado { get; private set; } = NotificationEstado.PendienteDeEnvio;

    public int IntentosRealizados { get; private set; }

    /// <summary><see langword="null"/> salvo que <see cref="Estado"/> sea
    /// <see cref="NotificationEstado.PendienteDeReintento"/> -- momento (UTC) a partir del cual
    /// <c>NotificationRetryJob</c> puede reclamar esta fila (calculado con
    /// <c>EventRetryBackoff.CalculateDelay</c>, F3-07, reutilizado sin cambios).</summary>
    public DateTime? ProximoReintentoUtc { get; private set; }

    public string? UltimoErrorMensaje { get; private set; }

    public DateTime? EnviadaAtUtc { get; private set; }

    /// <summary>Marca de lectura -- con sentido real solo para <see cref="NotificationChannel.InApp"/>
    /// (el canal Email no tiene forma de que este módulo sepa si el destinatario lo leyó en su cliente
    /// de correo).</summary>
    public DateTime? LeidoAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Notification(
        Guid id,
        Guid destinatarioUserId,
        string? destinatarioContacto,
        Guid? disparadoPorUserId,
        string codigoPlantilla,
        NotificationChannel canal,
        string locale,
        string? asunto,
        string cuerpo)
        : base(id)
    {
        DestinatarioUserId = destinatarioUserId;
        DestinatarioContacto = destinatarioContacto;
        DisparadoPorUserId = disparadoPorUserId;
        CodigoPlantilla = codigoPlantilla;
        Canal = canal;
        Locale = locale;
        Asunto = asunto;
        Cuerpo = cuerpo;
    }

    private Notification()
    {
    }

    public void MarcarOmitidaPorPreferencia() => Estado = NotificationEstado.OmitidaPorPreferencia;

    public void RegistrarEnvioExitoso(DateTime atUtc)
    {
        Estado = NotificationEstado.Enviada;
        EnviadaAtUtc = atUtc;
        ProximoReintentoUtc = null;
        UltimoErrorMensaje = null;

        RaiseDomainEvent(new NotificacionEnviadaIntegrationEvent(Id, DestinatarioUserId, CodigoPlantilla, Canal));
    }

    /// <summary>Registra un intento fallido TRANSITORIO y decide, con la misma política de reintentos
    /// reutilizada de F3-07 (<c>EventRetryPolicyOptions</c>/<c>EventRetryBackoff</c>, ver el
    /// <c>remarks</c> de <c>NotificationRetryJob</c> para por qué), si corresponde reintentar más tarde
    /// o darla por definitivamente fallida (agotó <c>EventRetryPolicyOptions.MaxAttempts</c>).</summary>
    public void RegistrarEnvioFallidoTransitorio(string error, DateTime atUtc, EventRetryPolicyOptions retryOptions)
    {
        IntentosRealizados++;
        UltimoErrorMensaje = error;

        if (EventRetryBackoff.IsExhausted(IntentosRealizados, retryOptions))
        {
            MarcarFallidaDefinitivamente(error);
            return;
        }

        Estado = NotificationEstado.PendienteDeReintento;
        ProximoReintentoUtc = atUtc.Add(EventRetryBackoff.CalculateDelay(IntentosRealizados, retryOptions));
    }

    /// <summary>Registra un intento fallido PERMANENTE -- nunca se reintenta, sin importar cuántos
    /// intentos lleve todavía disponibles la política de reintentos (mismo criterio que
    /// <c>EventPublishFailureKind.Permanent</c> de F3-07: un error de programación/dato inválido no se
    /// resuelve reintentando).</summary>
    public void RegistrarEnvioFallidoPermanente(string error)
    {
        IntentosRealizados++;
        MarcarFallidaDefinitivamente(error);
    }

    private void MarcarFallidaDefinitivamente(string error)
    {
        UltimoErrorMensaje = error;
        Estado = NotificationEstado.Fallida;
        ProximoReintentoUtc = null;

        RaiseDomainEvent(new NotificacionFallidaIntegrationEvent(Id, DestinatarioUserId, CodigoPlantilla, Canal, error));
    }

    public Result MarcarComoLeida(Guid actorUserId)
    {
        if (DestinatarioUserId != actorUserId)
        {
            return Result.Failure(new Error(
                "Notifications.Notificaciones.NoAutorizado",
                "Solo el destinatario puede marcar esta notificación como leída.", ErrorType.Forbidden));
        }

        LeidoAtUtc ??= DateTime.UtcNow;
        return Result.Success();
    }
}
