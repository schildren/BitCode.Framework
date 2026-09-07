using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>
/// Evento de integración de ejemplo que implementa <see cref="IHasPartitionKey"/> (F3-05), usado por
/// <c>Integration/KafkaEventPublisherPartitioningIntegrationTests.cs</c> para demostrar el criterio de
/// aceptación "Orden demostrado por partición".
/// </summary>
/// <remarks>
/// <see cref="Sequence"/> es la posición 0-based del evento dentro de la secuencia publicada por una
/// prueba concreta — permite verificar, después de consumir, que el orden de llegada coincide
/// exactamente con el orden de publicación (no hay ningún campo de <see cref="IIntegrationEvent"/> que
/// sirva para esto: <c>EventId</c> es aleatorio, <c>OccurredOnUtc</c> tiene resolución insuficiente para
/// distinguir eventos publicados en ráfaga). <see cref="Topic"/> sigue el mismo criterio que
/// <c>TestOrderCreatedIntegrationEvent</c>: cada prueba fija un valor único para no compartir tópico (y,
/// por lo tanto, mensajes) con otra prueba.
/// </remarks>
public sealed record TestPartitionedIntegrationEvent(int Sequence, string PartitionKeyValue, string Topic)
    : IntegrationEvent, IHasPartitionKey
{
    public override string EventType => $"Pedidos.PedidoCreado.F3-05Test.{Topic}";

    public string PartitionKey => PartitionKeyValue;
}
