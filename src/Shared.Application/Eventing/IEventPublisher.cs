namespace BitCode.Framework.Shared.Application.Eventing;

/// <summary>
/// Contrato de publicación de <see cref="IIntegrationEvent"/> (F3-01), agnóstico del broker
/// concreto (Kafka u otro, ADR <c>docs/adr/0005-mensajeria-kafka.md</c>, todavía "Proposed").
/// </summary>
/// <remarks>
/// Pensado como el punto de enganche que el relay de Outbox (F3-03) invoca DESPUÉS de leer un lote
/// de filas <c>OutboxMessage</c> pendientes (<c>ProcessedAtUtc IS NULL</c>) y reconstruir el
/// <see cref="IIntegrationEvent"/> correspondiente a cada una — nunca dentro de la transacción de
/// negocio que originó el evento (regla dura 3 de <c>docs/convenciones.md</c>: ninguna llamada de red
/// lenta, y un broker es justamente eso, debe ocurrir mientras una transacción de base de datos sigue
/// abierta). Un handler de comando NUNCA inyecta ni llama a <see cref="IEventPublisher"/>
/// directamente: la vía correcta para notificar un hecho de negocio a otro bounded context sigue
/// siendo <c>AggregateRoot&lt;TId&gt;.RaiseDomainEvent</c> (F1-23) más el relay de Outbox — este
/// contrato es la pieza que el relay usa del otro lado, no un atajo alternativo para el código de
/// aplicación.
///
/// La implementación concreta (adapter Kafka, F3-02) decide cómo serializa el evento, a qué
/// tópico/partición lo enruta (F3-05) y qué política de reintento aplica (F3-07) — nada de eso es
/// parte de este contrato, que solo declara la operación de publicación en sí. F3-01 no incluye
/// ninguna implementación: es responsabilidad de F3-02 en adelante.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>Publica un único evento de integración.</summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica un lote de eventos de integración. No garantiza atomicidad entre los eventos del lote
    /// frente a un broker externo (la atomicidad real ya la dio la transacción SQL que los persistió
    /// como <c>OutboxMessage</c> junto al cambio de negocio, F1-23) — cada evento puede alcanzar el
    /// broker o fallar de forma independiente; el llamador (relay de Outbox, F3-03) es quien decide
    /// cómo reintentar los que fallaron sin volver a publicar los que ya tuvieron éxito.
    /// </summary>
    Task PublishAsync(IEnumerable<IIntegrationEvent> integrationEvents, CancellationToken cancellationToken = default);
}
