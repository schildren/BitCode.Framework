using System.Diagnostics.Metrics;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Instrumentación OpenTelemetry (F3-10) de <see cref="OutboxBatchProcessor"/>: duración de cada ciclo de
/// sondeo, filas procesadas por resultado y publicaciones a DLQ del lado del relay de Outbox — la
/// contraparte "productor" de <c>KafkaEventingDiagnostics</c> (<c>Shared.Infrastructure.Messaging.Kafka</c>),
/// que instrumenta el broker en sí. La correlación de trazas de un evento publicado por
/// <see cref="OutboxBatchProcessor"/> queda cubierta transitivamente: <c>ProcessBatchAsync</c> publica cada
/// fila vía <c>IEventPublisher.PublishAsync</c> (Shared.Application), y la implementación concreta
/// (<c>KafkaEventPublisher</c>) ya abre su propio <c>Activity</c>/span de publish — este proyecto no
/// necesita (ni debe, para no acoplarse a un broker concreto) abrir un span adicional por evento.
/// </summary>
/// <remarks>
/// Mismo criterio de desacople que <c>KafkaEventingDiagnostics</c>: expone <see cref="MeterName"/> como
/// constante pública para que cualquier wiring de OpenTelemetry (<c>MeterProviderBuilder.AddMeter</c>)
/// pueda suscribirse sin que este proyecto (<c>Shared.Infrastructure.Persistence</c>) referencie
/// <c>Shared.Infrastructure.Observability</c>. Ver <c>docs/guia-observabilidad-eventos.md</c>.
/// </remarks>
public static class OutboxDiagnostics
{
    /// <summary>Nombre del <see cref="Meter"/> de este proyecto — usar con <c>MeterProviderBuilder.AddMeter</c>.</summary>
    public const string MeterName = "BitCode.Framework.Persistence.Outbox";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> BatchDuration = Meter.CreateHistogram<double>(
        "bitcode.outbox.batch.duration",
        unit: "ms",
        description: "Duración de un ciclo completo de OutboxBatchProcessor.ProcessBatchAsync (F3-03/F3-07).");

    private static readonly Counter<long> ProcessedRows = Meter.CreateCounter<long>(
        "bitcode.outbox.batch.rows",
        unit: "{row}",
        description: "Filas de OutboxMessage procesadas por ciclo, por resultado (published|skipped_internal|failed_retryable|exhausted). failed_retryable y exhausted son disjuntos entre sí (exhausted ya no reintenta).");

    private static readonly Counter<long> DeadLetteredRows = Meter.CreateCounter<long>(
        "bitcode.outbox.dlq.count",
        unit: "{row}",
        description: "Filas de OutboxMessage cuyo intento de publicación a un tópico dead-letter (F3-08, tras agotar reintentos) se registró, por resultado (success|failure).");

    /// <summary>
    /// Registra la duración del ciclo y la distribución de <paramref name="result"/> por resultado —
    /// <see cref="OutboxBatchResult.Failed"/> se reporta como <c>failed_retryable</c> (el subconjunto que
    /// TODAVÍA no agotó reintintos, ver remarks de <see cref="OutboxBatchResult"/>) para que
    /// <c>published + skipped_internal + failed_retryable + exhausted == claimed</c> sea siempre una
    /// partición disjunta, sin doble conteo entre <c>failed_retryable</c> y <c>exhausted</c>.
    /// </summary>
    internal static void RecordBatch(OutboxBatchResult result, double elapsedMs)
    {
        BatchDuration.Record(elapsedMs, new KeyValuePair<string, object?>("bitcode.outbox.claimed", result.Claimed));

        if (result.Published > 0)
        {
            ProcessedRows.Add(result.Published, new KeyValuePair<string, object?>("bitcode.outbox.outcome", "published"));
        }

        if (result.SkippedInternal > 0)
        {
            ProcessedRows.Add(result.SkippedInternal, new KeyValuePair<string, object?>("bitcode.outbox.outcome", "skipped_internal"));
        }

        var failedRetryable = result.Failed - result.Exhausted;
        if (failedRetryable > 0)
        {
            ProcessedRows.Add(failedRetryable, new KeyValuePair<string, object?>("bitcode.outbox.outcome", "failed_retryable"));
        }

        if (result.Exhausted > 0)
        {
            ProcessedRows.Add(result.Exhausted, new KeyValuePair<string, object?>("bitcode.outbox.outcome", "exhausted"));
        }
    }

    internal static void RecordDeadLetter(bool success)
    {
        DeadLetteredRows.Add(1, new KeyValuePair<string, object?>("bitcode.outbox.dlq.outcome", success ? "success" : "failure"));
    }
}
