using System.Diagnostics.Metrics;
using BitCode.Framework.Shared.Application.Eventing;
using Confluent.Kafka;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests;

/// <summary>
/// F3-10 ("Métricas de publish... error"): verifica, sin necesitar un broker real (mismo criterio que
/// <see cref="KafkaEventPublisherBrokerUnavailableTests"/> — un broker inalcanzable alcanza para forzar el
/// camino de fallo), que <see cref="KafkaEventPublisher.PublishAsync(IIntegrationEvent, CancellationToken)"/>
/// incrementa el contador/histograma de <see cref="KafkaEventingDiagnostics"/> con el resultado correcto,
/// vía <see cref="MeterListener"/> (mecanismo estándar de test de métricas de
/// <c>System.Diagnostics.Metrics</c> — no requiere ningún exporter real ni acceso a los instrumentos
/// internos, solo el nombre público <see cref="KafkaEventingDiagnostics.MeterName"/>).
/// </summary>
public class KafkaEventingDiagnosticsPublishMetricsTests
{
    [Fact]
    public async Task PublishAsync_BrokerUnavailable_RecordsPublishFailureMetric()
    {
        var options = new KafkaMessagingOptions
        {
            BootstrapServers = "127.0.0.1:19997", // puerto sin broker escuchando
            ClientId = "bitcode-tests-metrics-broker-down",
        };

        var producerConfig = KafkaClientConfigFactory.BuildProducerConfig(options);
        producerConfig.MessageTimeoutMs = 3000;
        producerConfig.SocketTimeoutMs = 1000;

        using var producer = new ProducerBuilder<string, byte[]>(producerConfig).Build();
        var publisher = new KafkaEventPublisher(producer);
        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Cliente sin broker", $"{Guid.NewGuid():N}");

        var publishCounterMeasurements = new List<(long Value, string? Outcome)>();
        var publishDurationMeasurements = new List<(double Value, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaEventingDiagnostics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "bitcode.messaging.publish.count")
            {
                publishCounterMeasurements.Add((measurement, FindTag(tags, "bitcode.messaging.outcome")));
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "bitcode.messaging.publish.duration")
            {
                publishDurationMeasurements.Add((measurement, FindTag(tags, "bitcode.messaging.outcome")));
            }
        });
        listener.Start();

        var act = () => publisher.PublishAsync(integrationEvent);
        await act.Should().ThrowAsync<ProduceException<string, byte[]>>();

        publishCounterMeasurements.Should().ContainSingle(m => m.Outcome == "failure" && m.Value == 1,
            "un fallo de publicación debe incrementar el contador con outcome=failure, nunca quedar silencioso");
        publishCounterMeasurements.Should().NotContain(m => m.Outcome == "success");
        publishDurationMeasurements.Should().ContainSingle(m => m.Outcome == "failure" && m.Value >= 0);
    }

    private static string? FindTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value as string;
            }
        }

        return null;
    }
}
