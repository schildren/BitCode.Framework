using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>
/// Cubre, a nivel de adapter (sin Testcontainers — no necesita un broker real, solo uno inalcanzable),
/// el caso "broker temporalmente no disponible" que la sección de pruebas obligatorias de la Fase 3
/// lista de forma general, incluida la clasificación transitorio/permanente y el cálculo de backoff que
/// le corresponden (F3-07, "Retries") — verifica que <see cref="KafkaEventPublisher"/> no absorbe el
/// fallo silenciosamente (cuando el broker no responde, <c>PublishAsync</c> falla con la excepción que
/// expone el cliente de Kafka en vez de completarse como si el mensaje hubiera llegado) Y que esa
/// excepción concreta, clasificada por <see cref="KafkaEventPublishFailureClassifier"/>, resulta
/// transitoria con un backoff calculado dentro de los límites esperados — sin depender de un broker real
/// que "se recupere" (ese round-trip completo, con reintento real exitoso, lo cubre
/// <c>OutboxPublisherIntegrationTests</c>/<c>OutboxPublisherRetryTests</c> contra un broker/SQL Server
/// reales).
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

    /// <summary>
    /// F3-07: la excepción real que expone <see cref="KafkaEventPublisher"/> contra un broker
    /// inalcanzable se clasifica como <see cref="EventPublishFailureKind.Transient"/> (reintentar tiene
    /// sentido: el broker puede volver en cualquier momento) — y el backoff calculado a partir de ese
    /// fallo cae dentro del rango esperado (0, tope exponencial del intento], nunca fuera de límites.
    /// </summary>
    [Fact]
    public async Task PublishAsync_BrokerUnavailable_ClassifiedTransientWithBackoffWithinExpectedBounds()
    {
        var options = new KafkaMessagingOptions
        {
            BootstrapServers = "127.0.0.1:19998",
            ClientId = "bitcode-tests-broker-down-retry-classification",
        };

        var producerConfig = KafkaClientConfigFactory.BuildProducerConfig(options);
        producerConfig.MessageTimeoutMs = 3000;
        producerConfig.SocketTimeoutMs = 1000;

        using var producer = new ProducerBuilder<string, byte[]>(producerConfig).Build();
        var publisher = new KafkaEventPublisher(producer);
        var classifier = new KafkaEventPublishFailureClassifier();
        var retryOptions = new EventRetryPolicyOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromMinutes(1),
        };

        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Clasificación de reintento", $"{Guid.NewGuid():N}");

        Exception? caught = null;
        try
        {
            await publisher.PublishAsync(integrationEvent);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull("un broker inalcanzable debe seguir fallando (mismo criterio que el test anterior)");
        var failureKind = classifier.Classify(caught!);
        failureKind.Should().Be(EventPublishFailureKind.Transient, "un broker inalcanzable es una condición que puede resolverse sola con el tiempo");

        // Primer reintento (intento 1): el tope exponencial es exactamente BaseDelay (base * 2^0).
        var firstAttemptUpperBound = retryOptions.BaseDelay;
        for (var i = 0; i < 50; i++)
        {
            var delay = EventRetryBackoff.CalculateDelay(1, retryOptions);
            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
            delay.Should().BeLessThanOrEqualTo(firstAttemptUpperBound);
        }

        EventRetryBackoff.IsExhausted(retryOptions.MaxAttempts, retryOptions).Should().BeTrue("al llegar a MaxAttempts intentos, ya no debe quedar margen");
        EventRetryBackoff.IsExhausted(retryOptions.MaxAttempts - 1, retryOptions).Should().BeFalse("un intento antes del límite todavía debe quedar margen");
    }
}
