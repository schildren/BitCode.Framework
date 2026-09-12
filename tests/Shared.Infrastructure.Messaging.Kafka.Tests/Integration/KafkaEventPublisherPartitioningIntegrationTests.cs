using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests.Integration;

/// <summary>
/// F3-05 ("Particionamiento"): criterio de aceptación literal "Orden demostrado por partición" contra un
/// broker Kafka real (Testcontainers, <see cref="KafkaContainerFixture"/>), con un tópico de MÚLTIPLES
/// particiones (a diferencia de <c>KafkaEventPublisherConsumerIntegrationTests</c>, que usa el tópico de
/// una sola partición que el broker auto-crea).
/// </summary>
/// <remarks>
/// Tres escenarios, cada uno el criterio de aceptación/prueba obligatoria correspondiente de F3-05:
/// <list type="bullet">
/// <item>Misma <c>PartitionKey</c> → misma partición → orden de consumo idéntico al de publicación
/// (semántica exigida de la Fase 3: "orden solo garantizado dentro de la partición definida").</item>
/// <item>Distinta <c>PartitionKey</c> → puede repartirse en particiones distintas (sin garantía de orden
/// relativo entre esos eventos, que es la semántica esperada, no un defecto).</item>
/// <item>Un evento SIN <see cref="BitCode.Framework.Shared.Application.Eventing.IHasPartitionKey"/> sigue
/// publicándose con <c>Key</c> = <c>EventId</c> (compatibilidad con F3-01/F3-02/F3-03/F3-04, sin cambios
/// de comportamiento para un evento que no participa de F3-05).</item>
/// </list>
/// </remarks>
[Collection(KafkaCollection.Name)]
public class KafkaEventPublisherPartitioningIntegrationTests(KafkaContainerFixture fixture)
{
    private const int PartitionCount = 3;

    private KafkaMessagingOptions BuildOptions() => new()
    {
        BootstrapServers = fixture.BootstrapServers,
        ClientId = "bitcode-tests-partitioning",
    };

    [Fact]
    public async Task PublishAsync_MismaPartitionKey_LlegaEnElMismoOrdenDePublicacion()
    {
        var topicSuffix = $"{Guid.NewGuid():N}";
        var partitionKey = $"pedido-{Guid.NewGuid():N}";
        var topic = await CreateMultiPartitionTopicAsync(topicSuffix);

        var options = BuildOptions();
        WaitForTopicMetadata(options, topic, PartitionCount, TimeSpan.FromSeconds(30));
        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);

        var events = Enumerable.Range(0, 10)
            .Select(i => new TestPartitionedIntegrationEvent(i, partitionKey, topicSuffix))
            .ToList();

        foreach (var evt in events)
        {
            await publisher.PublishAsync(evt);
        }

        var consumed = await ConsumeAllAsync(options, topic, events.Count, TimeSpan.FromSeconds(30));

        consumed.Should().HaveCount(events.Count);
        consumed.Select(r => r.Partition.Value).Distinct().Should()
            .ContainSingle("todos los eventos con la misma PartitionKey deben quedar en la misma partición del tópico");
        consumed.Select(DeserializeSequence).Should().Equal(
            events.Select(e => e.Sequence),
            "el orden de consumo dentro de una partición debe coincidir exactamente con el orden de publicación");
    }

    [Fact]
    public async Task PublishAsync_DistintaPartitionKey_PuedeTerminarEnParticionesDistintas()
    {
        var topicSuffix = $"{Guid.NewGuid():N}";
        var topic = await CreateMultiPartitionTopicAsync(topicSuffix);

        var options = BuildOptions();
        WaitForTopicMetadata(options, topic, PartitionCount, TimeSpan.FromSeconds(30));
        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);

        var events = Enumerable.Range(0, 20)
            .Select(i => new TestPartitionedIntegrationEvent(i, $"pedido-{Guid.NewGuid():N}", topicSuffix))
            .ToList();

        foreach (var evt in events)
        {
            await publisher.PublishAsync(evt);
        }

        var consumed = await ConsumeAllAsync(options, topic, events.Count, TimeSpan.FromSeconds(30));

        consumed.Should().HaveCount(events.Count);
        consumed.Select(r => r.Partition.Value).Distinct().Count().Should().BeGreaterThan(1,
            "20 eventos con PartitionKey distinta entre sí, contra un tópico de 3 particiones, deben poder repartirse en más de una partición (sin garantía de orden relativo entre ellos)");
    }

    [Fact]
    public async Task PublishAsync_SinPartitionKeyExplicito_SiguePublicandoConKeyIgualAlEventId()
    {
        var topicSuffix = $"{Guid.NewGuid():N}";
        var evt = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Cliente sin PartitionKey", topicSuffix);
        var topic = DefaultKafkaTopicNameResolver.Instance.ResolveTopicName(evt.EventType);

        var options = BuildOptions();
        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);

        // Este tópico NO se crea explícitamente con múltiples particiones (a diferencia de los otros dos
        // escenarios): el fallback a EventId no depende de cuántas particiones tenga el tópico, solo de
        // que TestOrderCreatedIntegrationEvent no implementa IHasPartitionKey.
        await publisher.PublishAsync(evt);

        var consumed = await ConsumeAllAsync(options, topic, expectedCount: 1, TimeSpan.FromSeconds(30));

        consumed.Should().ContainSingle();
        consumed[0].Message.Key.Should().Be(
            evt.EventId.ToString(),
            "sin PartitionKey explícito, KafkaEventPublisher sigue usando EventId como Key (fallback compatible con F3-02/F3-03/F3-04, sin ninguna garantía de orden)");
    }

    private async Task<string> CreateMultiPartitionTopicAsync(string topicSuffix)
    {
        var sample = new TestPartitionedIntegrationEvent(0, "seed", topicSuffix);
        var topic = DefaultKafkaTopicNameResolver.Instance.ResolveTopicName(sample.EventType);

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = fixture.BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = topic, NumPartitions = PartitionCount, ReplicationFactor = 1 },
        ]);

        return topic;
    }

    /// <summary>
    /// Espera a que la metadata del tópico recién creado (número de particiones incluido) se haya
    /// propagado al broker que atenderá al productor — evitar esto hace que las primeras publicaciones
    /// puedan usar temporalmente menos particiones de las esperadas mientras la metadata todavía se está
    /// actualizando.
    /// </summary>
    private void WaitForTopicMetadata(KafkaMessagingOptions options, string topic, int expectedPartitions, TimeSpan timeout)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();

        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(5));
            var topicMetadata = metadata.Topics.Find(t => t.Topic == topic);
            if (topicMetadata is not null && topicMetadata.Error.Code == ErrorCode.NoError
                && topicMetadata.Partitions.Count >= expectedPartitions)
            {
                return;
            }

            Thread.Sleep(200);
        }
    }

    private static int DeserializeSequence(ConsumeResult<string, byte[]> result)
        => KafkaIntegrationEventSerializer.Deserialize<TestPartitionedIntegrationEvent>(result.Message.Value).Sequence;

    private static async Task<List<ConsumeResult<string, byte[]>>> ConsumeAllAsync(
        KafkaMessagingOptions options, string topic, int expectedCount, TimeSpan overallTimeout)
    {
        var consumerConfig = KafkaClientConfigFactory.BuildConsumerConfig(options, $"grupo-{Guid.NewGuid():N}");
        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        consumer.Subscribe(topic);

        var results = new List<ConsumeResult<string, byte[]>>();
        var deadline = DateTime.UtcNow.Add(overallTimeout);
        while (results.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(2));
                if (result is not null && !result.IsPartitionEOF && result.Message is not null)
                {
                    results.Add(result);
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        consumer.Close();
        return results;
    }
}
