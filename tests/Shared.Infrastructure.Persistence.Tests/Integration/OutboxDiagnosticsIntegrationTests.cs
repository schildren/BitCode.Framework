using System.Diagnostics.Metrics;
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Outbox;
using BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Integration;

/// <summary>
/// F3-10 ("Métricas de publish, consume, error, lag y DLQ"): verifica, contra SQL Server real (donde vive
/// <see cref="OutboxMessage"/>), que <see cref="OutboxBatchProcessor"/> reporta <see cref="OutboxDiagnostics"/>
/// con el resultado correcto para una fila publicada con éxito y para una fila que agota reintentos (DLQ del
/// lado del relay de Outbox) — no necesita Kafka real: <see cref="RecordingEventPublisher"/> (reutilizado
/// de <see cref="OutboxPublisherIntegrationTests"/>) alcanza, el foco de este test es la instrumentación en
/// sí, no el round-trip contra un broker concreto (ver <c>OutboxPublisherIntegrationTests</c> para eso).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class OutboxDiagnosticsIntegrationTests(SqlServerContainerFixture sqlFixture)
{
    private string BuildIsolatedConnectionString([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        sqlFixture.BuildIsolatedConnectionString("ObxDiag", testName);

    private static async Task<ServiceProvider> BuildProviderAsync(
        string connectionString,
        IEventPublisher eventPublisher,
        OutboxPublisherOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedPersistence<MultiTenantTestDbContext>(connectionString);
        services.AddSingleton(eventPublisher);
        services.AddSingleton(options ?? new OutboxPublisherOptions());
        services.AddSingleton<IEventPublishFailureClassifier, DefaultEventPublishFailureClassifier>();
        services.AddScoped<OutboxBatchProcessor>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        return provider;
    }

    private static async Task<OutboxMessage> SeedOutboxMessageAsync(IServiceProvider provider, object domainEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Empty,
            EventType = domainEvent.GetType().AssemblyQualifiedName!,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
            OccurredAtUtc = DateTime.UtcNow,
        };

        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync();

        return message;
    }

    [Fact]
    public async Task ProcessBatchAsync_PublishSucceeds_RecordsPublishedRowMetric()
    {
        var publisher = new RecordingEventPublisher();
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), publisher);
        await SeedOutboxMessageAsync(provider, new OutboxPublisherTestEvent(Guid.NewGuid(), "Métricas de éxito"));

        using var recorder = new MetricRecorder();

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Published.Should().Be(1);
        }

        recorder.LongMeasurements("bitcode.outbox.batch.rows").Should().ContainSingle(m => m.Outcome == "published" && m.Value == 1);
        recorder.DoubleMeasurements("bitcode.outbox.batch.duration").Should().ContainSingle(m => m >= 0);
    }

    /// <summary>
    /// Una fila cuyo <c>EventType</c> ni siquiera puede resolverse a un tipo .NET (F3-07: error PERMANENTE,
    /// se agota en el primer intento) debe registrar tanto <c>bitcode.outbox.batch.rows</c>
    /// (outcome=exhausted) como <c>bitcode.outbox.dlq.count</c> (outcome=success, si hay
    /// <see cref="IDeadLetterPublisher"/> registrado) o quedar sin publicación a DLQ si no lo hay (este
    /// test no registra ninguno — verifica solo la métrica de fila agotada, la de DLQ per se ya la cubre
    /// <see cref="OutboxDeadLetterIntegrationTests"/>).
    /// </summary>
    [Fact]
    public async Task ProcessBatchAsync_UnresolvableEventType_RecordsExhaustedRowMetric()
    {
        var publisher = new RecordingEventPublisher();
        await using var provider = await BuildProviderAsync(BuildIsolatedConnectionString(), publisher);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MultiTenantTestDbContext>();
            context.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.Empty,
                EventType = "Tipo.Que.No.Existe, EnsambladoInexistente",
                PayloadJson = "{}",
                OccurredAtUtc = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        using var recorder = new MetricRecorder();

        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OutboxBatchProcessor>();
            var result = await processor.ProcessBatchAsync();
            result.Exhausted.Should().Be(1, "un tipo irresolvible es un error PERMANENTE (F3-07), se agota en el primer intento");
        }

        recorder.LongMeasurements("bitcode.outbox.batch.rows").Should().ContainSingle(m => m.Outcome == "exhausted" && m.Value == 1);
    }

    /// <summary>
    /// <see cref="MeterListener"/> de conveniencia — mismo patrón que
    /// <c>KafkaEventingObservabilityIntegrationTests.MetricRecorder</c> (Shared.Infrastructure.Messaging.Kafka.Tests),
    /// suscripto a <see cref="OutboxDiagnostics.MeterName"/>.
    /// </summary>
    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, long Value, string? Outcome)> _longMeasurements = [];
        private readonly List<(string Instrument, double Value)> _doubleMeasurements = [];

        public MetricRecorder()
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OutboxDiagnostics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                lock (_longMeasurements)
                {
                    _longMeasurements.Add((instrument.Name, measurement, FindTag(tags, "bitcode.outbox.outcome")));
                }
            });
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
            {
                lock (_doubleMeasurements)
                {
                    _doubleMeasurements.Add((instrument.Name, measurement));
                }
            });
            _listener.Start();
        }

        public IReadOnlyList<(long Value, string? Outcome)> LongMeasurements(string instrumentName)
        {
            lock (_longMeasurements)
            {
                return _longMeasurements.Where(m => m.Instrument == instrumentName).Select(m => (m.Value, m.Outcome)).ToList();
            }
        }

        public IReadOnlyList<double> DoubleMeasurements(string instrumentName)
        {
            lock (_doubleMeasurements)
            {
                return _doubleMeasurements.Where(m => m.Instrument == instrumentName).Select(m => m.Value).ToList();
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
