using System.Text;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Application.Inbox;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests.Integration;

/// <summary>
/// Criterio de aceptación literal de F3-09 ("Consumer continúa operando"): un mensaje "poison" (no se
/// puede deserializar a <see cref="TestOrderCreatedIntegrationEvent"/>) no debe bloquear la partición —
/// <see cref="KafkaEventConsumer{TEvent}"/> debe aislarlo a DLQ, confirmar su offset y seguir procesando
/// con normalidad el mensaje válido siguiente, contra un broker Kafka real (Testcontainers,
/// <see cref="KafkaContainerFixture"/>).
/// </summary>
[Collection(KafkaCollection.Name)]
public class KafkaEventConsumerPoisonMessageIntegrationTests(KafkaContainerFixture fixture)
{
    private KafkaMessagingOptions BuildOptions(string consumerGroupId) => new()
    {
        BootstrapServers = fixture.BootstrapServers,
        ClientId = "bitcode-poison-tests",
        ConsumerGroupId = consumerGroupId,
    };

    private static IServiceScopeFactory BuildScopeFactory(RecordingEventConsumer<TestOrderCreatedIntegrationEvent> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInboxMessageProcessor, InMemoryInboxMessageProcessor>();
        services.AddSingleton<IEventConsumer<TestOrderCreatedIntegrationEvent>>(handler);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task ConsumeAndHandleOnceAsync_PoisonMessageFollowedByValidMessage_IsolatesPoisonAndKeepsConsuming()
    {
        var topicSuffix = $"{Guid.NewGuid():N}";
        var eventType = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "x", topicSuffix).EventType;
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();

        // Mensaje poison: bytes que NO son un JSON válido en absoluto — no hay forma de que
        // KafkaIntegrationEventSerializer.Deserialize<TestOrderCreatedIntegrationEvent> lo interprete.
        var poisonPayload = Encoding.UTF8.GetBytes("esto no es JSON en absoluto {{{");
        var poisonTopic = DefaultKafkaTopicNameResolver.Instance.ResolveTopicName(eventType);
        await ProduceRawAsync(producer, poisonTopic, poisonPayload);

        // Mensaje válido publicado a continuación (misma partición: ambos van con clave null → partición
        // determinada por el broker, pero en un tópico de una sola partición recién creado quedan en la
        // única partición existente).
        var publisher = new KafkaEventPublisher(producer);
        var validEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Cliente válido", topicSuffix);
        await publisher.PublishAsync(validEvent);

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        var deadLetterPublisher = new KafkaDeadLetterPublisher(producer);
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(
            options,
            eventType,
            BuildScopeFactory(handler),
            deadLetterPublisher: deadLetterPublisher);

        // Primera llamada: consume el mensaje poison. No debe lanzar sin manejo, debe confirmar su
        // offset y retornar sin bloquear.
        var firstCallHandledSomething = await PollUntilTrueOrEmptyAsync(consumer, TimeSpan.FromSeconds(30));
        firstCallHandledSomething.Should().BeTrue("el mensaje poison debe aislarse (offset confirmado) en vez de colgar la partición");
        handler.ReceivedEvents.Should().BeEmpty("el mensaje poison nunca llega al handler de negocio");

        // Segunda llamada: la partición debe seguir avanzando y entregar el mensaje válido siguiente.
        var secondCallHandledSomething = await PollUntilTrueOrEmptyAsync(consumer, TimeSpan.FromSeconds(30));
        secondCallHandledSomething.Should().BeTrue("el consumer debe seguir operando y procesar el mensaje válido siguiente");
        handler.ReceivedEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(validEvent);

        // El mensaje poison debe haber llegado al tópico DLQ con el motivo esperado.
        var dlqTopic = poisonTopic + KafkaDeadLetterPublisher.DeadLetterTopicSuffix;
        using var dlqConsumer = new ConsumerBuilder<string, byte[]>(
            KafkaClientConfigFactory.BuildConsumerConfig(options, $"dlq-verify-{Guid.NewGuid():N}")).Build();
        dlqConsumer.Subscribe(dlqTopic);
        var dlqResult = await PollUntilConsumedAsync(dlqConsumer, TimeSpan.FromSeconds(30));

        dlqResult.Should().NotBeNull("el mensaje poison debe haberse publicado al tópico dead-letter");
        dlqResult!.Message.Value.Should().BeEquivalentTo(poisonPayload, "el payload dead-letter conserva los bytes originales sin transformar");
        GetHeaderString(dlqResult.Message.Headers, KafkaDeadLetterPublisher.ReasonHeader)
            .Should().StartWith("PoisonMessage", "el motivo debe distinguir un poison message de un agotamiento de reintentos de handler (F3-07)");
    }

    private static async Task ProduceRawAsync(IProducer<string, byte[]> producer, string topic, byte[] payload)
    {
        await producer.ProduceAsync(topic, new Message<string, byte[]> { Value = payload }).ConfigureAwait(false);
    }

    private static async Task<bool> PollUntilTrueOrEmptyAsync(KafkaEventConsumer<TestOrderCreatedIntegrationEvent> consumer, TimeSpan overallTimeout)
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
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        return false;
    }

    private static async Task<ConsumeResult<string, byte[]>?> PollUntilConsumedAsync(IConsumer<string, byte[]> consumer, TimeSpan overallTimeout)
    {
        var deadline = DateTime.UtcNow.Add(overallTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(2));
                if (result is { IsPartitionEOF: false, Message: not null })
                {
                    return result;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        return null;
    }

    private static string? GetHeaderString(Headers headers, string key) =>
        headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
