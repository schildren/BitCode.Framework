using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>Evento de integración de ejemplo para las pruebas del adapter Kafka (F3-02), mismo espíritu que <c>TestOrderCreatedIntegrationEvent</c> de <c>Shared.Application.Tests</c>.</summary>
/// <remarks>
/// <see cref="EventType"/> incluye <see cref="Topic"/>, que cada prueba de integración fija a un valor
/// único (por ejemplo, un <c>Guid</c>) — el tópico se deriva de <c>EventType</c> (F3-02), así que dos
/// pruebas que compartieran el mismo <c>EventType</c> compartirían el mismo tópico Kafka y, con
/// <c>AutoOffsetReset.Earliest</c>, un consumidor de una prueba podría leer mensajes publicados por
/// otra prueba anterior.
/// </remarks>
public sealed record TestOrderCreatedIntegrationEvent(Guid OrderId, string CustomerName, string Topic) : IntegrationEvent
{
    public override string EventType => $"Pedidos.PedidoCreado.F3-02Test.{Topic}";
}
