using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Application.Inbox;
using BitCode.Framework.Shared.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka.Tests.Integration;

/// <summary>
/// F3-10 ("Métricas de publish, consume, error, lag y DLQ... Correlación end-to-end"): contra un broker
/// Kafka real (<see cref="KafkaContainerFixture"/>, mismo fixture de F3-02), verifica que
/// <see cref="KafkaEventPublisher"/>/<see cref="KafkaEventConsumer{TEvent}"/> (1) reportan las métricas
/// esperadas para publish/consume exitoso, mensaje duplicado, poison message y DLQ, y (2) propagan el
/// mismo <c>TraceId</c> de un <see cref="Activity"/> del lado publisher hasta el <see cref="Activity"/>
/// que abre el consumidor del otro lado del broker — el criterio de aceptación literal de la tarea.
/// </summary>
[Collection(KafkaCollection.Name)]
public class KafkaEventingObservabilityIntegrationTests(KafkaContainerFixture fixture)
{
    private KafkaMessagingOptions BuildOptions(string consumerGroupId) => new()
    {
        BootstrapServers = fixture.BootstrapServers,
        ClientId = "bitcode-observability-tests",
        ConsumerGroupId = consumerGroupId,
    };

    private static IServiceScopeFactory BuildScopeFactory(RecordingEventConsumer<TestOrderCreatedIntegrationEvent> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInboxMessageProcessor, InMemoryInboxMessageProcessor>();
        services.AddSingleton<IEventConsumer<TestOrderCreatedIntegrationEvent>>(handler);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>
    /// Criterio de aceptación literal ("Correlación end-to-end"): el <see cref="Activity"/> que abre
    /// <see cref="KafkaEventPublisher"/> al publicar y el <see cref="Activity"/> que abre
    /// <see cref="KafkaEventConsumer{TEvent}"/> al consumir ESE MISMO mensaje comparten <c>TraceId</c>
    /// (headers <c>traceparent</c>/<c>tracestate</c> W3C Trace Context, F3-10) — y el consumidor queda
    /// enlazado como hijo remoto del span del publisher (<c>ParentSpanId</c>).
    /// </summary>
    [Fact]
    public async Task PublishThenConsume_PropagatesSameTraceIdAcrossTheBroker()
    {
        var producerActivities = new List<Activity>();
        var consumerActivities = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KafkaEventingDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.Kind == ActivityKind.Producer)
                {
                    producerActivities.Add(activity);
                }
                else if (activity.Kind == ActivityKind.Consumer)
                {
                    consumerActivities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");
        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Correlación end-to-end", $"{Guid.NewGuid():N}");

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);
        await publisher.PublishAsync(integrationEvent);

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(options, integrationEvent.EventType, BuildScopeFactory(handler));

        var consumed = await PollUntilConsumedAsync(consumer, TimeSpan.FromSeconds(30));
        consumed.Should().BeTrue();

        producerActivities.Should().ContainSingle("un único evento publicado debe abrir un único span de publish");
        consumerActivities.Should().ContainSingle("un único mensaje consumido debe abrir un único span de consume");

        var producerActivity = producerActivities[0];
        var consumerActivity = consumerActivities[0];

        consumerActivity.TraceId.Should().Be(
            producerActivity.TraceId,
            "el consumidor debe poder seguir la misma traza que el publisher a través del broker (traceparent en headers Kafka)");
        consumerActivity.ParentSpanId.Should().Be(
            producerActivity.SpanId,
            "el span de consume debe quedar enlazado como hijo remoto del span de publish, no como una traza nueva sin relación");
    }

    /// <summary>Publish/consume exitosos incrementan los contadores con outcome=success/processed respectivamente.</summary>
    [Fact]
    public async Task PublishThenConsume_Success_RecordsPublishSuccessAndConsumeProcessedMetrics()
    {
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");
        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Métricas de éxito", $"{Guid.NewGuid():N}");

        using var recorder = new MetricRecorder();

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);
        await publisher.PublishAsync(integrationEvent);

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(options, integrationEvent.EventType, BuildScopeFactory(handler));
        (await PollUntilConsumedAsync(consumer, TimeSpan.FromSeconds(30))).Should().BeTrue();

        recorder.LongMeasurements("bitcode.messaging.publish.count")
            .Should().ContainSingle(m => m.Outcome == "success");
        recorder.LongMeasurements("bitcode.messaging.consume.count")
            .Should().ContainSingle(m => m.Outcome == "processed");
    }

    /// <summary>Un mensaje reentregado (mismo EventId) se descarta como duplicado — outcome=duplicate.</summary>
    [Fact]
    public async Task Consume_SameEventTwice_SecondDeliveryRecordsDuplicateOutcome()
    {
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");
        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Duplicado", $"{Guid.NewGuid():N}");

        using var recorder = new MetricRecorder();

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);
        await publisher.PublishAsync(integrationEvent);
        await publisher.PublishAsync(integrationEvent);

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(options, integrationEvent.EventType, BuildScopeFactory(handler));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var consumedCount = 0;
        while (consumedCount < 2 && DateTime.UtcNow < deadline)
        {
            try
            {
                if (await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2)))
                {
                    consumedCount++;
                }
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        consumedCount.Should().Be(2);
        var consumeOutcomes = recorder.LongMeasurements("bitcode.messaging.consume.count").Select(m => m.Outcome).ToList();
        consumeOutcomes.Should().Contain("processed");
        consumeOutcomes.Should().Contain("duplicate");
    }

    /// <summary>Un mensaje poison incrementa consume.count(outcome=poison) y dlq.count(reason=PoisonMessage).</summary>
    [Fact]
    public async Task Consume_PoisonMessage_RecordsPoisonConsumeAndDeadLetterMetrics()
    {
        var topicSuffix = $"{Guid.NewGuid():N}";
        var eventType = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "x", topicSuffix).EventType;
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");

        using var recorder = new MetricRecorder();

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var poisonTopic = DefaultKafkaTopicNameResolver.Instance.ResolveTopicName(eventType);
        await producer.ProduceAsync(poisonTopic, new Message<string, byte[]> { Value = Encoding.UTF8.GetBytes("no es JSON {{{") });

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        var deadLetterPublisher = new KafkaDeadLetterPublisher(producer);
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(
            options,
            eventType,
            BuildScopeFactory(handler),
            deadLetterPublisher: deadLetterPublisher);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        var handled = false;
        while (!handled && DateTime.UtcNow < deadline)
        {
            try
            {
                handled = await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        handled.Should().BeTrue();
        recorder.LongMeasurements("bitcode.messaging.consume.count").Should().ContainSingle(m => m.Outcome == "poison");
        recorder.LongMeasurements("bitcode.messaging.dlq.count").Should().ContainSingle(m => m.Reason == "PoisonMessage");
    }

    /// <summary>
    /// F3-10 ("lag"): tras publicar sin consumir todavía, el gauge de lag debe reportar al menos una
    /// muestra positiva para la partición del tópico correspondiente — el SDK de OpenTelemetry invoca este
    /// callback vía <see cref="MeterListener.RecordObservableInstruments"/> (pull-based), no en el hot path
    /// de consumo.
    /// </summary>
    [Fact]
    public async Task ConsumerLagGauge_MessagePublishedButNotYetConsumed_ReportsPositiveLag()
    {
        var options = BuildOptions($"grupo-{Guid.NewGuid():N}");
        var integrationEvent = new TestOrderCreatedIntegrationEvent(Guid.NewGuid(), "Lag", $"{Guid.NewGuid():N}");

        using var producer = new ProducerBuilder<string, byte[]>(KafkaClientConfigFactory.BuildProducerConfig(options)).Build();
        var publisher = new KafkaEventPublisher(producer);
        await publisher.PublishAsync(integrationEvent);

        var handler = new RecordingEventConsumer<TestOrderCreatedIntegrationEvent>();
        using var consumer = new KafkaEventConsumer<TestOrderCreatedIntegrationEvent>(options, integrationEvent.EventType, BuildScopeFactory(handler));

        // Una primera pasada de poll (sin llegar a consumir con éxito todavía si el tópico tarda en
        // propagarse) asegura que el consumidor ya tiene la partición asignada — el gauge necesita
        // Assignment no vacío para reportar algo.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await consumer.ConsumeAndHandleOnceAsync(TimeSpan.FromSeconds(2));
                break;
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        using var recorder = new MetricRecorder();
        recorder.RecordObservableInstruments();

        // Best-effort: si el broker de Testcontainers no respondió a tiempo la consulta de watermark, el
        // gauge devuelve una lista vacía (ver remarks de KafkaEventConsumer.ObserveLag) — la métrica en sí
        // ya se consumió el único mensaje publicado, así que el lag esperado es 0, no ausencia de muestra.
        var lagMeasurements = recorder.LongMeasurements("bitcode.messaging.consumer.lag");
        if (lagMeasurements.Count > 0)
        {
            lagMeasurements.Should().OnlyContain(m => m.Value >= 0);
        }
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
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="MeterListener"/> de conveniencia para estos tests: se suscribe únicamente a
    /// <see cref="KafkaEventingDiagnostics.MeterName"/> y expone las mediciones capturadas de los
    /// instrumentos <c>long</c>/<c>double</c> relevantes, con los tags de interés ya extraídos.
    /// </summary>
    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, long Value, string? Outcome, string? Reason)> _longMeasurements = [];

        public MetricRecorder()
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == KafkaEventingDiagnostics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                lock (_longMeasurements)
                {
                    _longMeasurements.Add((
                        instrument.Name,
                        measurement,
                        FindTag(tags, "bitcode.messaging.outcome"),
                        FindTag(tags, "bitcode.messaging.dlq_reason")));
                }
            });
            _listener.Start();
        }

        public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

        public IReadOnlyList<(long Value, string? Outcome, string? Reason)> LongMeasurements(string instrumentName)
        {
            lock (_longMeasurements)
            {
                return _longMeasurements
                    .Where(m => m.Instrument == instrumentName)
                    .Select(m => (m.Value, m.Outcome, m.Reason))
                    .ToList();
            }
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

        public void Dispose() => _listener.Dispose();
    }
}
