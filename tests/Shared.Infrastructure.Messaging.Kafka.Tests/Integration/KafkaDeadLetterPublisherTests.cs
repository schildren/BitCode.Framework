using System.Text;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests.Integration;

/// <summary>
/// F3-08 (DLQ): <see cref="KafkaDeadLetterPublisher"/> publica al tópico <c>{tópico original}.dlq</c>
/// con los headers <c>bitcode-dlq-*</c> — verificado contra un broker Kafka real.
/// </summary>
[Collection(KafkaCollection.Name)]
public class KafkaDeadLetterPublisherTests(KafkaContainerFixture fixture)
{
    private KafkaMessagingOptions BuildOptions(string consumerGroupId) => new()
    {
        BootstrapServers = fixture.BootstrapServers,
        ClientId = "bitcode-dlq-tests",
        ConsumerGroupId = consumerGroupId,
    };

    [Fact]
    public async Task PublishAsync_PublishesToSuffixedTopic_WithMetadataHeaders()
    {
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");
        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaDeadLetterPublisher(producer);

        var eventType = $"Tests.DlqEvent.{Guid.NewGuid():N}";
        var expectedDlqTopic = DefaultKafkaTopicNameResolver.Instance.ResolveTopicName(eventType) + KafkaDeadLetterPublisher.DeadLetterTopicSuffix;
        var sourceMessageId = Guid.NewGuid().ToString();
        var payload = Encoding.UTF8.GetBytes("""{"foo":"bar"}""");
        var exhaustedAtUtc = DateTime.UtcNow;

        await publisher.PublishAsync(new DeadLetterEnvelope
        {
            EventType = eventType,
            Payload = payload,
            Reason = "Fallo simulado (prueba F3-08).",
            Attempts = 3,
            ExhaustedAtUtc = exhaustedAtUtc,
            SourceMessageId = sourceMessageId,
        });

        using var consumer = new ConsumerBuilder<string, byte[]>(
            KafkaClientConfigFactory.BuildConsumerConfig(options, $"dlq-verify-{Guid.NewGuid():N}")).Build();
        consumer.Subscribe(expectedDlqTopic);

        var result = await PollUntilConsumedAsync(consumer, TimeSpan.FromSeconds(30));

        result.Should().NotBeNull("KafkaDeadLetterPublisher debe publicar al tópico {original}.dlq");
        result!.Message.Value.Should().BeEquivalentTo(payload, "el payload dead-letter nunca transforma el original");
        result.Message.Key.Should().Be(sourceMessageId);
        GetHeaderString(result.Message.Headers, KafkaDeadLetterPublisher.OriginalEventTypeHeader).Should().Be(eventType);
        GetHeaderString(result.Message.Headers, KafkaDeadLetterPublisher.ReasonHeader).Should().Be("Fallo simulado (prueba F3-08).");
        GetHeaderString(result.Message.Headers, KafkaDeadLetterPublisher.AttemptsHeader).Should().Be("3");
        GetHeaderString(result.Message.Headers, KafkaDeadLetterPublisher.SourceMessageIdHeader).Should().Be(sourceMessageId);
        GetHeaderString(result.Message.Headers, KafkaDeadLetterPublisher.ExhaustedAtUtcHeader).Should().Be(exhaustedAtUtc.ToString("O"));
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
