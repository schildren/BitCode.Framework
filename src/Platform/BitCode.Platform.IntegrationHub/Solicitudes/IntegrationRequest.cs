using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

/// <summary>
/// Una solicitud saliente encolada hacia un <see cref="IntegrationConnector"/> (Fase 6, módulo 9:
/// "colas" del Plan Maestro). Reutiliza el patrón ya establecido por el Outbox (F3-03) en vez de
/// inventar una cola propia: esta fila SOLO se persiste en estado <see cref="IntegrationRequestEstado.PendienteDeEnvio"/>
/// -- ningún camino síncrono intenta la llamada HTTP en el momento de encolar (a diferencia de
/// <c>NotificationSender</c>, que sí intenta un primer envío síncrono); es
/// <c>Procesamiento.IntegrationOutboundProcessorJob</c>, corriendo en un ciclo posterior, quien procesa el
/// lote de filas pendientes contra el conector real. Ver <c>docs/guia-integration-hub.md</c>, sección
/// "Colas", para por qué esta tarea eligió ese diseño en vez de reutilizar el intento síncrono de
/// Notifications.
/// </summary>
/// <remarks>
/// <see cref="DisparadoPorUserId"/> (quien invocó <c>EnviarSolicitudIntegracionCommand</c>, si vino de un
/// actor humano) es un campo DELIBERADAMENTE separado de "quién configuró el <see cref="ConnectorId"/>"
/// (auditado en <c>CrearConectorCommand</c>, no acá) -- mismo criterio de no conflacionar identidades
/// distintas que ya corrigió Task Inbox (hallazgo Crítico, 2026-09-09): configurar un conector y disparar
/// una solicitud puntual contra él son dos operaciones, dos actores potencialmente distintos, nunca el
/// mismo campo.
///
/// Implementa <see cref="IHasConcurrencyToken"/> (F1-08) por el mismo motivo que <c>Notification</c>
/// (Fase 6, módulo 8): dos caminos independientes pueden mutar la misma fila sin coordinación entre sí
/// -- <c>IntegrationOutboundProcessorJob</c> reintentando esta fila en un ciclo, mientras (en un futuro
/// consumidor real) un operador podría cancelar/forzar la solicitud desde otro camino.
/// </remarks>
public sealed class IntegrationRequest : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, IHasConcurrencyToken
{
    public Guid ConnectorId { get; private set; }

    /// <summary>Payload de negocio INTERNO (JSON), tal cual lo entregó el llamador -- antes de aplicar
    /// el mapping campo-a-campo del conector. Se conserva sin transformar para poder reconstruir/depurar
    /// el payload externo si el mapping del conector cambia después de encolar la solicitud.</summary>
    public string PayloadInternoJson { get; private set; } = string.Empty;

    /// <summary><see langword="null"/> hasta el primer intento de procesamiento -- lo calcula
    /// <c>Mapping.IntegrationFieldMapper</c> a partir de <see cref="PayloadInternoJson"/> y el mapping
    /// vigente del conector en el momento de CADA intento (no se congela en el momento de encolar: si el
    /// mapping del conector cambia entre dos intentos de la misma solicitud, el reintento usa el mapping
    /// vigente al momento de reintentar).</summary>
    public string? PayloadExternoJson { get; private set; }

    public Guid? DisparadoPorUserId { get; private set; }

    public IntegrationRequestEstado Estado { get; private set; } = IntegrationRequestEstado.PendienteDeEnvio;

    public int IntentosRealizados { get; private set; }

    /// <summary><see langword="null"/> salvo que <see cref="Estado"/> sea
    /// <see cref="IntegrationRequestEstado.PendienteDeReintento"/> -- calculado con
    /// <c>EventRetryBackoff.CalculateDelay</c> (F3-07, reutilizado sin cambios), mismo diseño que
    /// <c>Notification.ProximoReintentoUtc</c>.</summary>
    public DateTime? ProximoReintentoUtc { get; private set; }

    public string? UltimoErrorMensaje { get; private set; }

    public int? UltimoCodigoHttp { get; private set; }

    public DateTime? EnviadaAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public IntegrationRequest(Guid id, Guid connectorId, string payloadInternoJson, Guid? disparadoPorUserId)
        : base(id)
    {
        ConnectorId = connectorId;
        PayloadInternoJson = payloadInternoJson;
        DisparadoPorUserId = disparadoPorUserId;
    }

    private IntegrationRequest()
    {
    }

    public void RegistrarPayloadExterno(string payloadExternoJson) => PayloadExternoJson = payloadExternoJson;

    public void RegistrarEnvioExitoso(DateTime atUtc, int codigoHttp, string connectorCodigo)
    {
        Estado = IntegrationRequestEstado.Enviada;
        EnviadaAtUtc = atUtc;
        ProximoReintentoUtc = null;
        UltimoErrorMensaje = null;
        UltimoCodigoHttp = codigoHttp;

        RaiseDomainEvent(new SolicitudIntegracionEnviadaIntegrationEvent(Id, ConnectorId, connectorCodigo));
    }

    /// <summary>Registra un intento fallido TRANSITORIO y decide, con la misma política de reintentos
    /// reutilizada de F3-07 (<c>EventRetryPolicyOptions</c>/<c>EventRetryBackoff</c>), si corresponde
    /// reintentar más tarde o darla por definitivamente fallida (agotó
    /// <c>EventRetryPolicyOptions.MaxAttempts</c>). Mismo diseño que
    /// <c>Notification.RegistrarEnvioFallidoTransitorio</c>.</summary>
    public void RegistrarEnvioFallidoTransitorio(
        string error, int? codigoHttp, DateTime atUtc, EventRetryPolicyOptions retryOptions, string connectorCodigo)
    {
        IntentosRealizados++;
        UltimoErrorMensaje = error;
        UltimoCodigoHttp = codigoHttp;

        if (EventRetryBackoff.IsExhausted(IntentosRealizados, retryOptions))
        {
            MarcarFallidaDefinitivamente(error, connectorCodigo);
            return;
        }

        Estado = IntegrationRequestEstado.PendienteDeReintento;
        ProximoReintentoUtc = atUtc.Add(EventRetryBackoff.CalculateDelay(IntentosRealizados, retryOptions));
    }

    /// <summary>Registra un intento fallido PERMANENTE -- nunca se reintenta, sin importar cuántos
    /// intentos lleve todavía disponibles la política de reintentos (por ejemplo, el conector respondió
    /// 4xx: reintentar el mismo payload contra el mismo endpoint no cambia el resultado).</summary>
    public void RegistrarEnvioFallidoPermanente(string error, int? codigoHttp, string connectorCodigo)
    {
        IntentosRealizados++;
        UltimoCodigoHttp = codigoHttp;
        MarcarFallidaDefinitivamente(error, connectorCodigo);
    }

    private void MarcarFallidaDefinitivamente(string error, string connectorCodigo)
    {
        UltimoErrorMensaje = error;
        Estado = IntegrationRequestEstado.Fallida;
        ProximoReintentoUtc = null;

        RaiseDomainEvent(new SolicitudIntegracionFallidaIntegrationEvent(Id, ConnectorId, connectorCodigo, error));
    }
}
