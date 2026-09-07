using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests.Integration;

/// <summary>
/// Criterio de aceptación literal de F3-02 ("Pruebas con broker real"): publicar un evento de
/// integración con <see cref="KafkaEventPublisher"/> y verificar que puede consumirse del tópico
/// correspondiente con <see cref="KafkaEventConsumer{TEvent}"/>, contra un broker Kafka real
/// (Testcontainers, <see cref="KafkaContainerFixture"/>).
/// </summary>
[Collection(KafkaCollection.Name)]
public class KafkaEventPublisherConsumerIntegrationTests(KafkaContainerFixture fixture)
{
    private KafkaMessagingOptions BuildOptions(string consumerGroupId) => new()
    {
        BootstrapServers = fixture.BootstrapServers,
        ClientId = "bitcode-tests",
        ConsumerGroupId = consumerGroupId,
    };

    [Fact]
    public async Task PublishAsync_ThenConsume_RoundTripsTheSameEvent()
    {
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");
        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Cliente de prueba", $"{Guid.NewGuid():N}");

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);

        // Se publica ANTES de crear el consumidor: el tópico (derivado de EventType) todavía no
        // existe en un broker recién levantado y ProduceAsync es quien lo crea (auto-creación por
        // defecto del broker de Testcontainers.Kafka) — suscribirse a un tópico inexistente hace que
        // Confluent.Kafka falle con "Unknown topic or partition" en vez de esperar a que exista.
        await publisher.PublishAsync(integrationEvent);

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(
            options,
            integrationEvent.EventType,
            handler);

        var consumed = await PollUntilConsumedAsync(consumer, TimeSpan.FromSeconds(30));

        consumed.Should().BeTrue("el mensaje publicado debe poder consumirse del tópico correspondiente a EventType");
        handler.ReceivedEvents.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(integrationEvent);
    }

    [Fact]
    public async Task PublishAsync_Batch_AllEventsAreConsumed()
    {
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");
        var topic = $"{Guid.NewGuid():N}";
        var eventType = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "x", topic).EventType;

        var events = Enumerable.Range(0, 5)
            .Select(i => new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), $"Cliente {i}", topic))
            .ToList();

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);

        await publisher.PublishAsync(events);

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(options, eventType, handler);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (handler.ReceivedEvents.Count < events.Count && DateTime.UtcNow < deadline)
        {
            try
            {
                await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        handler.ReceivedEvents.Should().HaveCount(events.Count);
        handler.ReceivedEvents.Select(e => e.OrderId).Should().BeEquivalentTo(events.Select(e => e.OrderId));
    }

    private static async Task<bool> PollUntilConsumedAsync(KafkaEventConsumer<TestOrderCreatedIntegrationEvent> consumer, TimeSpan overallTimeout)
    {
        var deadline = DateTime.UtcNow.Add(overallTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2)))
                {
                    return true;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                // El tópico recién fue creado por el ProduceAsync anterior; la metadata puede tardar
                // en propagarse al broker que atiende este consumidor. Reintenta dentro del mismo
                // overallTimeout en vez de fallar la prueba por esta condición transitoria.
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        return false;
    }
}
