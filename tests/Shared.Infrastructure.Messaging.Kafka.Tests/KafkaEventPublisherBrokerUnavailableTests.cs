using Confluent.Kafka;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>
/// Cubre, a nivel de adapter (sin Testcontainers — no necesita un broker real, solo uno inalcanzable),
/// el caso "broker temporalmente no disponible" que la sección de pruebas obligatorias de la Fase 3
/// lista de forma general. La clasificación de error transitorio/permanente y la política de reintento
/// con backoff son F3-07 ("Retries") — esta prueba solo verifica que <see cref="KafkaEventPublisher"/>
/// no absorbe el fallo silenciosamente: cuando el broker no responde, <c>PublishAsync</c> falla con la
/// excepción que expone el cliente de Kafka en vez de completarse como si el mensaje hubiera llegado.
/// </summary>
public class KafkaEventPublisherBrokerUnavailableTests
{
    [Fact]
    public async Task PublishAsync_BrokerUnavailable_ThrowsInsteadOfSucceedingSilently()
    {
        var options = new KafkaMessagingOptions
        {
            BootstrapServers = "127.0.0.1:19999", // puerto sin broker escuchando
            ClientId = "bitcode-tests-broker-down",
        };

        var producerConfig = KafkaClientConfigFactory.BuildProducerConfig(options);
        // Timeouts cortos para que la prueba falle rápido en vez de esperar los defaults de
        // Confluent.Kafka (message.timeout.ms por defecto = 300000 ms).
        producerConfig.MessageTimeoutMs = 3000;
        producerConfig.SocketTimeoutMs = 1000;

        using var producer = new ProducerBuilder<string, byte[]>(producerConfig).Build();
        var publisher = new KafkaEventPublisher(producer);

        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Cliente sin broker", $"{Guid.NewGuid():N}");

        var act = () => publisher.PublishAsync(integrationEvent);

        await act.Should().ThrowAsync<ProduceException<string, byte[]>>();
    }
}
