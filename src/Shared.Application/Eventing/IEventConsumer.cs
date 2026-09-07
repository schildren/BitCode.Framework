namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Contrato de manejo de un <see cref="IIntegrationEvent"/> concreto (F3-01), agnóstico del broker
/// que lo entregó.
/// </summary>
/// <remarks>
/// Pensado para ser invocado por un futuro consumidor real de mensajería (adapter Kafka, F3-02;
/// consumer base de Inbox, F3-04) DESPUÉS de que <c>IInboxMessageProcessor.ProcessAsync</c> (F1-24)
/// determine que el mensaje no es un duplicado ya procesado — el mismo patrón que
/// <c>docs/convenciones.md</c> ya documenta para F1-24: <c>ConsumeAsync</c> es el <c>handler</c> que
/// se pasa a <c>ProcessAsync</c>, nunca se ejecuta el efecto de negocio del evento "a mano" fuera de
/// ese mecanismo. Debe modificar el estado a través del mismo <c>DbContext</c>/<c>IUnitOfWork</c> de
/// scope (por ejemplo, vía <c>IRepository</c>) sin llamar a <c>SaveChangesAsync</c> por su cuenta —
/// igual que cualquier <c>handler</c> de <see cref="BitCode.Framework.Shared.Application.Inbox.IInboxMessageProcessor"/>.
///
/// La entrega es "al menos una vez" y el procesamiento debe ser idempotente (semántica exigida de la
/// Fase 3, sección "Semántica exigida" del Plan Maestro): un <see cref="IEventConsumer{TEvent}"/> no
/// puede asumir que <see cref="ConsumeAsync"/> se invoca exactamente una vez por evento — la
/// deduplicación real la da el Inbox (F1-24/F3-04), no este contrato.
/// </remarks>
/// <typeparam name="TEvent">Tipo concreto de evento de integración que este consumidor sabe manejar.</typeparam>
public interface IEventConsumer<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>Ejecuta el efecto de negocio correspondiente a <paramref name="integrationEvent"/>.</summary>
    Task ConsumeAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
